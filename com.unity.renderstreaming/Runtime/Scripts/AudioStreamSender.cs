using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.WebRTC;
using UnityEngine;

namespace Unity.RenderStreaming
{
    /// <summary>
    /// 
    /// </summary>
    public enum AudioStreamSource
    {
        /// <summary>
        /// 
        /// </summary>
        AudioListener = 0,
        /// <summary>
        /// 
        /// </summary>
        AudioSource = 1,
        /// <summary>
        /// 
        /// </summary>
        Microphone = 2
    }

    /// <summary>
    /// Attach AudioListerner or AudioSource
    /// </summary>
    [AddComponentMenu("Render Streaming/Audio Stream Sender")]
    public class AudioStreamSender : StreamSenderBase
    {
        static readonly uint s_defaultMinBitrate = 0;
        static readonly uint s_defaultMaxBitrate = 200;

        [SerializeField]
        private AudioStreamSource m_Source;

        [SerializeField]
        private AudioListener m_AudioListener;

        [SerializeField]
        private AudioSource m_AudioSource;

        [SerializeField]
        private int m_MicrophoneDeviceIndex;

        [SerializeField]
        private bool m_AutoRequestUserAuthorization = true;

        [SerializeField, Codec]
        private AudioCodecInfo m_Codec;

        [SerializeField, Bitrate(0, 1000)]
        private Range m_Bitrate = new Range(s_defaultMinBitrate, s_defaultMaxBitrate);

        private int m_sampleRate = 0;

        private AudioStreamSourceImpl m_sourceImpl = null;

        /// workaround.
        private Action<float[], int, int> m_onAudioFilterRead = null;

        private int m_frequency = 48000;

        /// <summary>
        /// 
        /// </summary>
        public AudioStreamSource source
        {
            get { return m_Source; }
            set
            {
                if (isPlaying)
                    throw new InvalidOperationException("Can not change this parameter after the streaming is started.");
                m_Source = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public AudioCodecInfo codec
        {
            get { return m_Codec; }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint minBitrate
        {
            get { return m_Bitrate.min; }
        }

        /// <summary>
        /// 
        /// </summary>
        public uint maxBitrate
        {
            get { return m_Bitrate.max; }
        }

        /// <summary>
        /// The index of WebCamTexture.devices.
        /// </summary>
        public int sourceDeviceIndex
        {
            get { return m_MicrophoneDeviceIndex; }
            set
            {
                if (isPlaying)
                    throw new InvalidOperationException("Can not change this parameter after the streaming is started.");
                m_MicrophoneDeviceIndex = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public AudioSource audioSource
        {
            get { return m_AudioSource; }
            set
            {
                if (isPlaying)
                    throw new InvalidOperationException("Can not change this parameter after the streaming is started.");
                m_AudioSource = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public AudioListener audioListener
        {
            get { return m_AudioListener; }
            set
            {
                if (isPlaying)
                    throw new InvalidOperationException("Can not change this parameter after the streaming is started.");
                m_AudioListener = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        static public IEnumerable<AudioCodecInfo> GetAvailableCodecs()
        {
            var excludeCodecMimeType = new[] { "audio/CN", "audio/telephone-event" };
            var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Audio);
            return capabilities.codecs.Where(codec => !excludeCodecMimeType.Contains(codec.mimeType)).Select(codec => new AudioCodecInfo(codec));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="minBitrate"></param>
        /// <param name="maxBitrate"></param>
        public void SetBitrate(uint minBitrate, uint maxBitrate)
        {
            if (minBitrate > maxBitrate)
                throw new ArgumentException("The maxBitrate must be greater than minBitrate.", "maxBitrate");
            m_Bitrate.min = minBitrate;
            m_Bitrate.max = maxBitrate;
            foreach (var transceiver in Transceivers.Values)
            {
                RTCError error = transceiver.Sender.SetBitrate(m_Bitrate.min, m_Bitrate.max);
                if (error.errorType != RTCErrorType.None)
                    Debug.LogError(error.message);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="codec"></param>
        public void SetCodec(AudioCodecInfo codec)
        {
            m_Codec = codec;
            foreach (var transceiver in Transceivers.Values)
            {
                if (!string.IsNullOrEmpty(transceiver.Mid))
                    continue;
                if (transceiver.Sender.Track.ReadyState == TrackState.Ended)
                    continue;

                var codecs = new AudioCodecInfo[] { m_Codec };
                RTCErrorType error = transceiver.SetCodecPreferences(SelectCodecCapabilities(codecs).ToArray());
                if (error != RTCErrorType.None)
                    throw new InvalidOperationException($"Set codec is failed. errorCode={error}");
            }
        }

        internal IEnumerable<RTCRtpCodecCapability> SelectCodecCapabilities(IEnumerable<AudioCodecInfo> codecs)
        {
            return RTCRtpSender.GetCapabilities(TrackKind.Audio).SelectCodecCapabilities(codecs);
        }

        protected virtual void Awake()
        {
            OnStartedStream += _OnStartedStream;
            OnStoppedStream += _OnStoppedStream;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            m_sourceImpl?.Dispose();
            m_sourceImpl = null;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            m_sampleRate = AudioSettings.outputSampleRate;
        }

        void _OnStartedStream(string connectionId)
        {
        }

        void _OnStoppedStream(string connectionId)
        {
            m_sourceImpl?.Dispose();
            m_sourceImpl = null;
        }

        internal override WaitForCreateTrack CreateTrack()
        {
            m_sourceImpl?.Dispose();
            m_sourceImpl = CreateAudioStreamSource();
            return m_sourceImpl.CreateTrack();
        }


        AudioStreamSourceImpl CreateAudioStreamSource()
        {
            switch (m_Source)
            {
                case AudioStreamSource.AudioListener:
                    var source = new AudioStreamSourceAudioListener(this);
                    // todo:: workaround.
                    m_onAudioFilterRead = source.OnAudioFilterRead;
                    return source;
                case AudioStreamSource.AudioSource:
                    return new AudioStreamSourceAudioSource(this);
                case AudioStreamSource.Microphone:
                    return new AudioStreamSourceMicrophone(this);
            }
            throw new InvalidOperationException("");
        }


        protected override void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            base.OnDisable();
        }

        /// workaround.
        /// todo: Should add AudioStreamTrack supports AudioListener
        protected virtual void OnAudioFilterRead(float[] data, int channels)
        {
            // todo: Should add AudioStreamTrack supports AudioListener
            if (m_Source == AudioStreamSource.AudioListener && m_onAudioFilterRead != null)
                m_onAudioFilterRead(data, channels, m_sampleRate);
        }

        abstract class AudioStreamSourceImpl : IDisposable
        {
            public AudioStreamSourceImpl(AudioStreamSender parent)
            {
            }

            public abstract WaitForCreateTrack CreateTrack();
            public abstract void Dispose();
        }

        class AudioStreamSourceAudioListener : AudioStreamSourceImpl
        {
            AudioStreamTrack m_audioTrack;

            public AudioStreamSourceAudioListener(AudioStreamSender parent) : base(parent)
            {
                // todo: Should add AudioStreamTrack supports AudioListener
                if (!parent.GetComponent<AudioListener>())
                    throw new InvalidOperationException("Audio Listener have to be set the same gameObject.");
            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                m_audioTrack = new AudioStreamTrack();
                instruction.Done(m_audioTrack);
                return instruction;
            }

            public override void Dispose()
            {
                m_audioTrack = null;
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceAudioListener()
            {
                Dispose();
            }

            public void OnAudioFilterRead(float[] data, int channels, int sampleRate)
            {
                NativeArray<float> nativeArray = new NativeArray<float>(data, Allocator.Temp);
                try
                {
                    m_audioTrack?.SetData(ref nativeArray, channels, sampleRate);
                }
                // todo(kazuki):: Should catch only ObjectDisposedException but
                // AudioStreamTrack also throws NullReferenceException.
                catch (Exception)
                {
                }
                finally
                {
                    nativeArray.Dispose();
                }
            }
        }

        class AudioStreamSourceAudioSource : AudioStreamSourceImpl
        {
            AudioSource m_audioSource;
            public AudioStreamSourceAudioSource(AudioStreamSender parent) : base(parent)
            {
                m_audioSource = parent.m_AudioSource;
                if (m_audioSource == null)
                    throw new InvalidOperationException("The audioSource is not assigned.");

            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                instruction.Done(new AudioStreamTrack(m_audioSource));
                return instruction;
            }

            public override void Dispose()
            {
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceAudioSource()
            {
                Dispose();
            }
        }
        class AudioStreamSourceMicrophone : AudioStreamSourceImpl
        {
            int m_deviceIndex;
            bool m_autoRequestUserAuthorization;
            int m_frequency;
            string m_deviceName;
            AudioSource m_audioSource;
            AudioStreamSender m_parent;

            public AudioStreamSourceMicrophone(AudioStreamSender parent) : base(parent)
            {
                int deviceIndex = parent.m_MicrophoneDeviceIndex;
                if (deviceIndex < 0 || Microphone.devices.Length <= deviceIndex)
                    throw new ArgumentOutOfRangeException("deviceIndex", deviceIndex, "The deviceIndex is out of range");
                m_parent = parent;
                m_deviceIndex = deviceIndex;
                m_frequency = parent.m_frequency;
                m_autoRequestUserAuthorization = parent.m_AutoRequestUserAuthorization;
            }

            public override WaitForCreateTrack CreateTrack()
            {
                var instruction = new WaitForCreateTrack();
                m_parent.StartCoroutine(CreateTrackCoroutine(instruction));
                return instruction;
            }

            IEnumerator CreateTrackCoroutine(WaitForCreateTrack instruction)
            {
                if (m_autoRequestUserAuthorization)
                {
                    AsyncOperation op = Application.RequestUserAuthorization(UserAuthorization.Microphone);
                    yield return op;
                }
                if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                    throw new InvalidOperationException("Call Application.RequestUserAuthorization before creating track with Microphone.");

                m_deviceName = Microphone.devices[m_deviceIndex];
                Microphone.GetDeviceCaps(m_deviceName, out int minFreq, out int maxFreq);
                var micClip = Microphone.Start(m_deviceName, true, 1, m_frequency);

                // set the latency to “0” samples before the audio starts to play.
                yield return new WaitUntil(() => Microphone.GetPosition(m_deviceName) > 0);

                /// todo: Throw exception if gameObject already has the AudioSource.
                /// To fix this, fix the issue of AudioStreamTrack first.
                m_audioSource = m_parent.gameObject.GetComponent<AudioSource>();
                if(m_audioSource == null)
                    m_audioSource = m_parent.gameObject.AddComponent<AudioSource>();

                m_audioSource.clip = micClip;
                m_audioSource.loop = true;
                m_audioSource.Play();

                instruction.Done(new AudioStreamTrack(m_audioSource));
            }

            public override void Dispose()
            {
                if (m_audioSource != null)
                {
                    m_audioSource.Stop();
                    var clip = m_audioSource.clip;
                    if (clip != null)
                    {
                        Destroy(clip);
                    }
                    m_audioSource.clip = null;

                    /// todo: AudioCustomFilter should be removed before destroying m_audioSource because
                    /// AudioSource is the RequiredComponent by AudioCustomFilter. But AudioStreamTrack removes
                    /// the AudioCustomFilter asyncnouslly. So we got the error log below.
                    /// "Can't remove AudioSource because AudioCustomFilter (Script) depends on it"

                    // Destroy(m_audioSource);
                    m_audioSource = null;
                }
                if (Microphone.IsRecording(m_deviceName))
                    Microphone.End(m_deviceName);
                GC.SuppressFinalize(this);
            }

            ~AudioStreamSourceMicrophone()
            {
                Dispose();
            }
        }
    }
}
