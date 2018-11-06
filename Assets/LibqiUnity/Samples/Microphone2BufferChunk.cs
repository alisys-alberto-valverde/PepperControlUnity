using System;
using UnityEngine;

public class Microphone2BufferChunk : MonoBehaviour
{
    //音拾えてるか確認したいときにセットするためのオーディオソース(nullの場合は再生しない)
    public AudioSource audioSourceDebugOutput = null;

    private const int recordingFrequencyHz = 44100;
    private const int recordingLengthSec = 10;
    private const int blockSampleSize = 2205;
    private const float bufferGetIntervalSec = 0.05f;

    //use default microphone
    private const string _deviceName = "";
    private bool _validMicrophoneExists = false;

    private AudioClip _audioClip = null;

    private float[] _soundBuffer = new float[blockSampleSize];
    private float _deltaTime = 0f;
    private int _deltaTimeCount = 0;

    void Start()
    {
        _validMicrophoneExists = (Microphone.devices.Length > 0);
        if (_validMicrophoneExists)
        {
            _audioClip = Microphone.Start(_deviceName, false, recordingLengthSec, recordingFrequencyHz);
            PlayAudioSourceIfEnabled();
        }
    }

    void Update()
    {
        if (!_validMicrophoneExists) return;

        if (_deltaTime > bufferGetIntervalSec * (_deltaTimeCount + 1))
        {
            _audioClip.GetData(_soundBuffer, blockSampleSize * _deltaTimeCount);
            _deltaTimeCount++;
            OnRecorded(_soundBuffer);
        }

        if (!Microphone.IsRecording(_deviceName))
        {
            _deltaTime = 0f;
            _deltaTimeCount = 0;
            _audioClip = Microphone.Start(
                _deviceName, false, recordingLengthSec, recordingFrequencyHz);
            PlayAudioSourceIfEnabled();
        }
    }

    void OnDestroy()
    {
        if (Microphone.IsRecording(_deviceName))
        {
            Microphone.End(_deviceName);
        }
    }

    private void PlayAudioSourceIfEnabled()
    {
        if (audioSourceDebugOutput == null) return;

        audioSourceDebugOutput.Stop();
        audioSourceDebugOutput.clip = _audioClip;
        audioSourceDebugOutput.PlayDelayed(0.1f);
    }

    public event EventHandler<WaveSoundRecordedEventArgs> Recorded;

    private void OnRecorded(float[] buffer)
    {
        if (Recorded != null)
        {
            Recorded(this, new WaveSoundRecordedEventArgs(buffer));
        }
    }
}

public class WaveSoundRecordedEventArgs : EventArgs
{
    public WaveSoundRecordedEventArgs(float[] buffer)
    {
        _buffer = new float[buffer.Length];
        Array.Copy(buffer, _buffer, buffer.Length);
    }

    private readonly float[] _buffer;
    public float[] Buffer
    {
        get { return _buffer; }
    }
}
