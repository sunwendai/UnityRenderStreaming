using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;

namespace Unity.RenderStreaming
{
    /// <summary>
    ///
    /// </summary>
    public abstract class StreamSenderBase : MonoBehaviour, IStreamSender
    {
        internal class WaitForCreateTrack : CustomYieldInstruction
        {
            public MediaStreamTrack Track { get { return m_track; } }

            MediaStreamTrack m_track;

            bool m_keepWaiting = true;

            public override bool keepWaiting { get { return m_keepWaiting; } }

            public WaitForCreateTrack() { }

            public void Done(MediaStreamTrack track)
            {
                m_track = track;
                m_keepWaiting = false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        public IReadOnlyDictionary<string, RTCRtpTransceiver> Transceivers => m_transceivers;

        /// <summary>
        /// 
        /// </summary>
        public OnStartedStreamHandler OnStartedStream { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public OnStoppedStreamHandler OnStoppedStream { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        internal abstract WaitForCreateTrack CreateTrack();


        internal virtual void ReplaceTrack(MediaStreamTrack track)
        {
            if (track == null)
                throw new ArgumentNullException("track", "This argument must be not null.");

            if (m_track == Track)
                throw new ArgumentException("track", "The value of this argument has already been set.");

            /// todo:: If not disposing the old track here, the app will crash.
            /// This problem is caused by the MediaStreamTrack when it is destroyed on the thread other than the main thread.
            m_track?.Dispose();

            m_track = Track;
            foreach (var transceiver in Transceivers.Values)
            {
                transceiver.Sender.ReplaceTrack(m_track);
            }
        }

        internal void SetTrack(MediaStreamTrack track)
        {
            if (track == null)
                throw new ArgumentNullException("track", "This argument must be not null.");

            if (m_track != null)
                throw new InvalidOperationException("Track is not null.");
            m_track = track;
        }

        private MediaStreamTrack m_track;

        private Dictionary<string, RTCRtpTransceiver> m_transceivers =
            new Dictionary<string, RTCRtpTransceiver>();

        /// <summary>
        ///
        /// </summary>
        public MediaStreamTrack Track => m_track;

        /// <summary>
        /// 
        /// </summary>
        public bool isPlaying
        {
            get
            {
                if (!Application.isPlaying)
                    return false;
                foreach (var transceiver in Transceivers.Values)
                {
                    if (string.IsNullOrEmpty(transceiver.Mid))
                        continue;
                    if (transceiver.Sender.Track.ReadyState == TrackState.Ended)
                        continue;
                    return true;
                }
                return false;
            }
        }

        protected virtual void OnDestroy()
        {
            m_track?.Dispose();
            m_track = null;
        }

        protected virtual void OnEnable()
        {
            if (m_track?.ReadyState == TrackState.Live)
            {
                m_track.Enabled = true;
            }
        }

        protected virtual void OnDisable()
        {
            if (m_track?.ReadyState == TrackState.Live)
            {
                m_track.Enabled = false;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="sender"></param>
        public virtual void SetTransceiver(string connectionId, RTCRtpTransceiver transceiver)
        {
            if (connectionId == null)
                throw new ArgumentNullException("connectionId is null");
            if (transceiver == null)
            {
                m_transceivers.Remove(connectionId);
                OnStoppedStream?.Invoke(connectionId);
                if (!m_transceivers.Any())
                {
                    m_track.Dispose();
                    m_track = null;
                }
            }
            else
            {
                m_transceivers.Add(connectionId, transceiver);
                OnStartedStream?.Invoke(connectionId);
            }
        }
    }
}
