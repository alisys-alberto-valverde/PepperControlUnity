using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Baku.LibqiDotNet;
using Baku.LibqiDotNet.Service;
using Libqi = Baku.LibqiDotNet.Libqi;

public class PepperController : MonoBehaviour
{

    #region consts

    //Pepper側で使用: 音声ダウンロード時にPepper側に登録する変数へ割り当てる名前(一貫してれば他の名前でもOK)
    const string DownloadSoundModuleName = "UnitySoundDownloader";
    //Pepper側で使用: PepperのAPIで前頭部の集音マイクを表すマジックナンバー
    const int DownloadChannelFront = 3;
    
    //PepperとUnityの両方で使用: Pepperからダウンロードする音声のサンプリングレート(16000か48000)
    const int DownloadSoundFrequency = 48000;

    //Unity側で使用: AudioClipに割り当てる名前
    const string DownloadSoundClipName = "SoundFromPepper";
    //Unity側で使用: AudioClipの長さ。適当に長さを取ってAudioClipをリングバッファ的に使う
    const int DownloadSoundClipLengthSec = 10;
    //Unity側で使用: ダウンロードした音声を再生するときのレイテンシ
    const float DownloadSoundPlayLatency = 0.2f;

    #endregion

    #region いじりたい外部設定各位

    /// <summary>
    /// interactableを操作するターゲットとしての接続ボタン。
    /// ロボット未接続時はイネーブル、接続済みならディスエーブル。
    /// </summary>
    public Button connectButton;
    /// <summary>
    /// interactableを操作するターゲットとしての切断ボタン。
    /// ロボット未接続時はディスエーブル、接続済みならイネーブル。
    /// </summary>
    public Button disconnectButton;
    /// <summary>
    /// interactableを操作するターゲットとしての制御GUIグループ。
    /// ロボットに接続するとイネーブル、切断するとディスエーブル。
    /// </summary>
    public CanvasGroup controllerGuiGroup;
    /// <summary>
    /// Pepperから拾ってきた音を再生するAudioClipを挿し込むためのAudioSource。
    /// 設定しない場合はアタッチされてるAudioSourceを拾って勝手に使う。
    /// </summary>
    public AudioSource audioSourceToPlayPepperSound;

    /// <summary>頭部中央のタッチセンサー状態を示す文字を出力するテキスト</summary>
    public Text textHeadTouchState;
    /// <summary>左手のタッチセンサー状態を示す文字を出力するテキスト</summary>
    public Text textLeftHandTouchState;
    /// <summary>右手のタッチセンサー状態を示す文字を出力するテキスト</summary>
    public Text textRightHandTouchState;

    #endregion

    private QiSession _session;

    //services to use
    private ALAudioDevice _audioDevice;
    private ALTextToSpeech _tts;
    private ALMotion _motion;
    private ALMemory _memory;
    private ALLeds _leds;

    private string _targetIp = "";

    void Start()
    {
        //_session = new QiSessionFactory().CreateLibqiSession();
        _session = new QiSessionFactory().CreateSocketIoSession();
        if (audioSourceToPlayPepperSound == null)
        {
            audioSourceToPlayPepperSound = GetComponent<AudioSource>();
        }
    }

    void Update()
    {
        if (textHeadTouchState != null)
        {
            textHeadTouchState.text = (IsHeadTouched ? "ON" : "OFF");
        }

        if (textLeftHandTouchState != null)
        {
            textLeftHandTouchState.text = (IsLHandTouched ? "ON" : "OFF");
        }

        if (textRightHandTouchState != null)
        {
            textRightHandTouchState.text = (IsRHandTouched ? "ON" : "OFF");
        }
    }

    void OnDestroy()
    {
        if (_session != null && _session.IsConnected)
        {
            _session.Close();
            _session = null;
        }
    }

    #region Connection
    public void UpdateTargetIp(string ip)
    {
        _targetIp = ip;
        Debug.Log("Update Target IP: " + ip);
    }

    public void ConnectToPepper()
    {
        Debug.Log("Connect, target IP is: " + _targetIp);

        if (string.IsNullOrEmpty(_targetIp)) return;

        //_session.Connect(_targetIp).Wait();
        _session.Connect(_targetIp);

        if (!_session.IsConnected)
        {
            Debug.Log("Failed to connect");
            return;
        }
        else
        {
            Debug.Log("Connected");
        }

        //移動や音声合成で使うサブモジュールもロード
        InitializeServices(_session);
        StartObserveTouchSensors();

        //接続時用のinteractable設定に切り替え
        SetGuiInteractabilities(true);
        InitializeGUITextInfo();
    }
    public void DisconnectFromPepper()
    {
        Debug.Log("Disconnect called");
        StopSoundDownload();
        StopSoundUpload();
        StopObserveTouchSensors();

        DisposeServices();
        _session.Close();

        SetGuiInteractabilities(false);
        InitializeGUITextInfo();
    }

    private void InitializeServices(QiSession session)
    {
        _tts = ALTextToSpeech.CreateService(session);
        _motion = ALMotion.CreateService(session);
        _memory = ALMemory.CreateService(session);
        _leds = ALLeds.CreateService(session);
        try
        {
            _audioDevice = ALAudioDevice.CreateService(session);
        }
        catch(InvalidOperationException)
        {
            Debug.Log("Failed to get ALAudioDevice: Service is not on the robot. Are you use a simulator?");
        }
    }
    private void DisposeServices()
    {
        _tts = null;
        _leds = null;
        _memory = null;
        _motion = null;
        _audioDevice = null;
    }

    private void SetGuiInteractabilities(bool isConnected)
    {
        if (connectButton != null)
        {
            //接続済なら接続ボタンは使えない
            connectButton.interactable = !isConnected;
        }
        if (disconnectButton != null)
        {
            //接続すると切断ボタンが使える
            disconnectButton.interactable = isConnected;
        }
        if (controllerGuiGroup != null)
        {
            //コントローラパネルは接続中だけ使える
            controllerGuiGroup.interactable = isConnected;
        }
    }
    private void InitializeGUITextInfo()
    {
        if (textHeadTouchState != null) textHeadTouchState.text = "OFF";
        if (textLeftHandTouchState != null) textLeftHandTouchState.text = "OFF";
        if (textRightHandTouchState != null) textRightHandTouchState.text = "OFF";
    }
    #endregion

    #region Motor On/Off: Simple Function call
    public void MotorOn()
    {
        Debug.Log("MotorOn called");
        _motion.WakeUp();
    }
    public void MotorOff()
    {
        Debug.Log("MotorOff Called");
        _motion.Rest();
    }
    #endregion

    #region Text To Speech
    private string _saySentence = "";
    public void UpdateSentence(string sentence)
    {
        _saySentence = sentence;
        Debug.Log("Update Sentence: " + sentence);
    }
    public void SaySentence()
    {
        Debug.Log("Say sentence: " + _saySentence);
        _tts.SetLanguage("English");
        _tts.Say(_saySentence);
    }
    #endregion

    #region Move by wheels
    private const float MoveSpeedFraction = 0.3f;
    public void StartMoveForward()
    {
        Debug.Log("StartMoveForward called");
        _motion.MoveToward(MoveSpeedFraction, 0f, 0f);
    }
    public void StartMoveBack()
    {
        Debug.Log("StartMoveBack called");
        _motion.MoveToward(-MoveSpeedFraction, 0f, 0f);
    }
    public void StartMoveRight()
    {
        Debug.Log("StartMoveRight called");
        _motion.MoveToward(0f, -MoveSpeedFraction, 0);
    }
    public void StartMoveLeft()
    {
        Debug.Log("StartMoveLeft called");
        _motion.MoveToward(0f, MoveSpeedFraction, 0f);
    }
    public void StopMove()
    {
        Debug.Log("StopMove called");
        _motion.StopMove();
    }
    #endregion

    #region Memory Event Detection (タッチセンサー)
    private IQiSignal _subscriberHead;
    private IQiSignal _subscriberLHand;
    private IQiSignal _subscriberRHand;

    private bool _isHeadTouched;
    private bool _isLHandTouched;
    private bool _isRHandTouched;
    private object _isHeadTouchedLock = new object();
    private object _isLHandTouchedLock = new object();
    private object _isRHandTouchedLock = new object();
    private bool IsHeadTouched
    {
        get { lock (_isHeadTouchedLock) return _isHeadTouched; }
        set { lock (_isHeadTouchedLock) _isHeadTouched = value; }
    }
    private bool IsLHandTouched
    {
        get { lock (_isLHandTouchedLock) return _isLHandTouched; }
        set { lock (_isLHandTouchedLock) _isLHandTouched = value; }
    }
    private bool IsRHandTouched
    {
        get { lock (_isRHandTouchedLock) return _isRHandTouched; }
        set { lock (_isRHandTouchedLock) _isRHandTouched = value; }
    }

    public void StartObserveTouchSensors()
    {
        if (_memory == null) return;

        _subscriberHead = _memory.Subscriber("MiddleTactilTouched");
        _subscriberLHand = _memory.Subscriber("HandLeftBackTouched");
        _subscriberRHand = _memory.Subscriber("HandRightBackTouched");

        _subscriberHead.Connect(OnMiddleTactilTouched);
        _subscriberLHand.Connect(OnHandLeftBackTouched);
        _subscriberRHand.Connect(OnHandRightBackTouched);

    }
    public void StopObserveTouchSensors()
    {
        if (_memory == null) return;

        _subscriberHead.DisconnectAsync(OnMiddleTactilTouched);
        _subscriberLHand.DisconnectAsync(OnHandLeftBackTouched);
        _subscriberRHand.DisconnectAsync(OnHandRightBackTouched);

        _subscriberHead = null;
        _subscriberLHand = null;
        _subscriberRHand = null;

        IsHeadTouched = false;
        IsLHandTouched = false;
        IsRHandTouched = false;
    }

    private void OnMiddleTactilTouched(IQiResult res)
    {
        if (textHeadTouchState == null || res.Count == 0) return;
        //res: タプルが渡される。このイベントでは整数"1"が触られた状態、"0"が触られなくなった状態を表す。
        int state = res[0].ToInt32();
        IsHeadTouched = (state != 0);
    }
    private void OnHandLeftBackTouched(IQiResult res)
    {
        if (textLeftHandTouchState == null || res.Count == 0) return;
        int state = res[0].ToInt32();
        IsLHandTouched = (state != 0);
    }
    private void OnHandRightBackTouched(IQiResult res)
    {
        if (textRightHandTouchState == null || res.Count == 0) return;
        int state = res[0].ToInt32();
        IsRHandTouched = (state != 0);
    }
    #endregion

    #region Sound Upload

    //NOTE: 音声アップロードは現時点では動作しません。
    //(.NET Console Appでは別ライブラリで動作できていました。おそらくUnityマルチスレッド対応周りの作り込みが甘いのが原因です)
    //NOTE: currently sound upload DOES NOT work.

    public void UpdateSoundUploadState(bool enabled)
    {
        if (enabled)
        {
            StartSoundUpload();
        }
        else
        {
            StopSoundDownload();
        }
    }
    private void StartSoundUpload()
    {
        var recorder = GetComponent<Microphone2BufferChunk>();
        if (recorder == null) return;

        SetSoundOutputPreferences();
        recorder.Recorded += OnMicrophoneSoundBlockRecorded;
    }
    private void StopSoundUpload()
    {
        var recorder = GetComponent<Microphone2BufferChunk>();
        if (recorder != null)
        {
            recorder.Recorded -= OnMicrophoneSoundBlockRecorded;
        }
    }
    private void SetSoundOutputPreferences()
    {
        if (_audioDevice == null) return;
        _audioDevice.SetParameter("outputSampleRate", 44100);
    }
    private void OnMicrophoneSoundBlockRecorded(object sender, WaveSoundRecordedEventArgs e)
    {
        if (_audioDevice == null) return;

        //書式が違うので直してから投げる。
        //Unity : モノラル float[] (-1.0f ~ 1.0f)
        //Pepper: ステレオ short[] (left, right, left, right,..) をbyte[]に変換したやつ
        byte[] bufferAsByte = new byte[e.Buffer.Length * 4];
        for (int i = 0; i < e.Buffer.Length; i++)
        {
            short sample = (short)(e.Buffer[i] * short.MaxValue);
            byte[] sampleBytes = BitConverter.GetBytes(sample);
            Array.Copy(sampleBytes, 0, bufferAsByte, i * 4, 2);
            Array.Copy(sampleBytes, 0, bufferAsByte, i * 4 + 2, 2);
        }

        //よくない方法: この方法だと呼び出し元がブロックする(下手するとUIが固まる)
        //_audioDevice.SendRemoteBufferToOutput(bufferAsByte.Length / 4, bufferAsByte);
        //よい方法: こっちは非同期呼び出しなので手元でブロックされない
        //_audioDevice.SourceService["sendRemoteBufferToOutput"].Post(bufferAsByte.Length / 4, bufferAsByte);
        _audioDevice.SendRemoteBufferToOutputAsync(bufferAsByte.Length / 4, bufferAsByte);
    }
    #endregion

    #region Sound Download

    //NOTE: 音声ダウンロードは現時点で動作しません。
    //(.NET Console Appでは別ライブラリで動作できていました。おそらくUnityマルチスレッド対応周りの作り込みが甘いのが原因です)
    //NOTE: Currently sound download DOES NOT work.

    //音声のダウンロードをハンドリングするインスタンス
    private Libqi.QiObject _soundDownloader;
    //_soundDownloaderをリモートに登録した際に得られるID(登録解除で必要)
    private uint _soundDownloaderRegisterId;
    //Pepperからおろしてきた音声を突っこむオーディオクリップ
    private AudioClip _clipDownloadedSound;
    //Pepperからおろした音声をセットすべき位置。リングバッファ的にループさせることに注意。
    private int _clipPositionToSetData = 0;

    public void UpdateSoundDownloadState(bool enabled)
    {
        if (enabled)
        {
            StartSoundDownload();
        }
        else
        {
            StopSoundDownload();
        }
    }
    private void StartSoundDownload()
    {
        //no player
        if (audioSourceToPlayPepperSound == null) return;
        //not available
        if (_audioDevice == null) return;
        //already downloading
        if (_soundDownloader != null) return;

        //Unity側の再生設定
        _clipDownloadedSound = AudioClip.Create(DownloadSoundClipName, 
            DownloadSoundFrequency * DownloadSoundClipLengthSec, 1, DownloadSoundFrequency, true);
        audioSourceToPlayPepperSound.clip = _clipDownloadedSound;

        //Pepper側のダウンロード開始
        _audioDevice.SetClientPreferences(DownloadSoundModuleName, DownloadSoundFrequency, 3, 0);
        _soundDownloader = CreateSoundDownloader();
        //_soundDownloaderRegisterId = (uint)_session
        //    .RegisterService(DownloadSoundModuleName, _soundDownloader)
        //    .GetUInt64(0);
        _soundDownloaderRegisterId = _session
            .RegisterService(DownloadSoundModuleName, _soundDownloader);
        _audioDevice.Subscribe(DownloadSoundModuleName);

        //レイテンシ無いと安定しない(ハズな)ので注意
        audioSourceToPlayPepperSound.loop = true;
        audioSourceToPlayPepperSound.PlayDelayed(DownloadSoundPlayLatency);
    }
    private void StopSoundDownload()
    {
        //no players
        if (audioSourceToPlayPepperSound == null) return;
        //not available
        if (_audioDevice == null) return;
        //not downloading
        if (_soundDownloader == null) return;

        //Unity resources
        audioSourceToPlayPepperSound.Stop();
        _clipDownloadedSound = null;
        _clipPositionToSetData = 0;

        //Pepper resources
        _audioDevice.Unsubscribe(DownloadSoundModuleName);
        _session.UnregisterService(_soundDownloaderRegisterId);
        _soundDownloader = null;
        _soundDownloaderRegisterId = 0;
    }
    private Libqi.QiObject CreateSoundDownloader()
    {
        var objBuilder = Libqi.QiObjectBuilder.Create();
        //ここの名前は変更したらダメ(Pepper側がこの名前でメソッド探してコールバックで呼ぶため)
        objBuilder.AdvertiseMethod("processRemote::v(iimm)",
            (sig, arg) =>
            {
                byte[] rawData = arg[3].ToBytes();
                SetSoundChunkToClip(rawData);
                return Libqi.QiValue.Void;
            });
        return objBuilder.BuildObject();
    }
    private void SetSoundChunkToClip(byte[] rawData)
    {
        //無いはずだけど一応
        if (_clipDownloadedSound == null) return;
        if (audioSourceToPlayPepperSound == null) return;

        float[] samples = new float[rawData.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToSingle(rawData, i * 2);
        }

        _clipDownloadedSound.SetData(samples, _clipPositionToSetData);
        //循環バッファ処理
        _clipPositionToSetData += samples.Length;
        if (_clipPositionToSetData > _clipDownloadedSound.samples)
        {
            _clipPositionToSetData = 0;
        }

    }
    #endregion

    //Eye LEDs
    public void SetLeds(int presetId)
    {
        Debug.Log(string.Format("SetLeds called, id: ", presetId));

        EyeLedPresets preset = (EyeLedPresets)presetId;
        //set color (0x00RRGGBB format)
        int encodedRgb =
            preset == EyeLedPresets.Black ? 0x000000 :
            preset == EyeLedPresets.White ? 0xffffff :
            preset == EyeLedPresets.Red ? 0xff0000 :
            preset == EyeLedPresets.Green ? 0x00ff00 :
            preset == EyeLedPresets.Blue ? 0x0000ff :
            0x000000;

        _leds.FadeRGB("FaceLeds", encodedRgb, 1.0f);
    }

    enum EyeLedPresets
    {
        Black = 0,
        White = 1,
        Red = 2,
        Green = 3,
        Blue = 4
    }

}
