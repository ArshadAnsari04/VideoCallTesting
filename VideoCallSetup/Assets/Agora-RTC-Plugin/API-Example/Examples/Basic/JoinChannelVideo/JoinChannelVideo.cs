// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using Agora.Rtc;
using io.agora.rtc.demo;
using System.Collections;
using System.Collections.Generic;
using Meta.XR.Samples;
using PassthroughCameraSamples;

namespace Agora_RTC_Plugin.API_Example.Examples.Basic.JoinChannelVideo
{
    public class JoinChannelVideo : MonoBehaviour
    {
        [FormerlySerializedAs("appIdInput")]
        [SerializeField]
        private AppIdInput _appIdInput;

        [Header("_____________Basic Configuration_____________")]
        [FormerlySerializedAs("APP_ID")]
        [SerializeField]
        private string _appID = "";

        [FormerlySerializedAs("TOKEN")]
        [SerializeField]
        private string _token = "";

        [FormerlySerializedAs("CHANNEL_NAME")]
        [SerializeField]
        private string _channelName = "";

        public Text LogText;
        internal Logger Log;
        internal IRtcEngine RtcEngine = null;

        public Dropdown _videoDeviceSelect;
        private IVideoDeviceManager _videoDeviceManager;
        private DeviceInfo[] _videoDeviceInfos;
        public Dropdown _areaSelect;
        public GameObject _videoQualityItemPrefab;

        // Passthrough Camera fields for Quest 3
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private Text m_debugText;
        [SerializeField] private RawImage m_image;

        // Platform-specific video source
        private WebCamTexture _videoTexture;
        private Texture2D _textureBuffer;

        // Use this for initialization
        private void Start()
        {
            LoadAssetData();
            PrepareAreaList();
            if (CheckAppId())
            {
                RtcEngine = Agora.Rtc.RtcEngine.CreateAgoraRtcEngine();
            }

#if UNITY_IOS || UNITY_ANDROID
            var text = GameObject.Find("VideoCanvas/Scroll View/Viewport/Content/VideoDeviceManager").GetComponent<Text>();
            text.text = "Video device manager not supported on this platform";
            GameObject.Find("VideoCanvas/Scroll View/Viewport/Content/VideoDeviceButton").SetActive(false);
            GameObject.Find("VideoCanvas/Scroll View/Viewport/Content/deviceIdSelect").SetActive(false);
            GameObject.Find("VideoCanvas/Scroll View/Viewport/Content/VideoSelectButton").SetActive(false);
#endif
            InitEngine();
            Invoke("JoinChannel", 4);

#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(InitializeQuestPassthrough());
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR
            StartCoroutine(InitializeWindowsWebcam());
#endif
        }

        // Initialize Quest 3 Passthrough Camera
        private IEnumerator InitializeQuestPassthrough()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            while (m_webCamTextureManager.WebCamTexture == null)
            {
                yield return null;
            }
            _videoTexture = m_webCamTextureManager.WebCamTexture;
            _textureBuffer = new Texture2D(_videoTexture.width, _videoTexture.height, TextureFormat.RGBA32, false);
            m_debugText.text += "\nWebCamTexture Object ready and playing.";
            m_image.texture = _videoTexture;
            StartCoroutine(StreamVideoToAgora());
#endif
            yield break;
        }

        // Initialize Windows Webcam
        private IEnumerator InitializeWindowsWebcam()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length > 0)
            {
                _videoTexture = new WebCamTexture(devices[0].name);
                _videoTexture.Play();
                while (!_videoTexture.isPlaying)
                {
                    yield return null;
                }
                _textureBuffer = new Texture2D(_videoTexture.width, _videoTexture.height, TextureFormat.RGBA32, false);
                m_debugText.text += "\nWindows Webcam initialized.";
                m_image.texture = _videoTexture;
                StartCoroutine(StreamVideoToAgora());
            }
            else
            {
                m_debugText.text += "\nNo webcam found on Windows.";
            }
#endif
            yield break;
        }

        // Stream video to Agora
        private IEnumerator StreamVideoToAgora()
        {
            while (true)
            {
                if (_videoTexture != null && _videoTexture.isPlaying)
                {
                    _textureBuffer.SetPixels(_videoTexture.GetPixels());
                    _textureBuffer.Apply();

                    byte[] videoData = _textureBuffer.GetRawTextureData();
                    ExternalVideoFrame frame = new ExternalVideoFrame
                    {
                        type = VIDEO_BUFFER_TYPE.VIDEO_BUFFER_RAW_DATA,
                        format = VIDEO_PIXEL_FORMAT.VIDEO_PIXEL_RGBA,
                        buffer = videoData,
                        stride = _videoTexture.width,
                        height = _videoTexture.height,
                        timestamp = (long)(Time.time * 1000)
                    };

                    int ret = RtcEngine.PushVideoFrame(frame);
                    if (ret != 0)
                    {
                        Log.UpdateLog("PushVideoFrame failed: " + ret);
                    }
                }
                yield return new WaitForEndOfFrame();
            }
        }

        // Update is called once per frame
        private void Update()
        {
            PermissionHelper.RequestMicrophontPermission();
            PermissionHelper.RequestCameraPermission();
#if UNITY_ANDROID && !UNITY_EDITOR
            m_debugText.text = PassthroughCameraPermissions.HasCameraPermission == true ? "Permission granted." : "No permission granted.";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR
            m_debugText.text = "Running on Windows platform.";
#endif
        }

        //Show data in AgoraBasicProfile
        [ContextMenu("ShowAgoraBasicProfileData")]
        private void LoadAssetData()
        {
            if (_appIdInput == null) return;
            _appID = _appIdInput.appID;
            _token = _appIdInput.token;
            _channelName = _appIdInput.channelName;
        }

        private bool CheckAppId()
        {
            Log = new Logger(LogText);
            return Log.DebugAssert(_appID.Length > 10, "Please fill in your appId in API-Example/profile/appIdInput.asset");
        }

        private void PrepareAreaList()
        {
            int index = 0;
            var areaList = new List<Dropdown.OptionData>();
            var enumNames = Enum.GetNames(typeof(AREA_CODE));
            foreach (var name in enumNames)
            {
                areaList.Add(new Dropdown.OptionData(name));
                if (name == "AREA_CODE_GLOB")
                {
                    index = areaList.Count - 1;
                }
            }
            _areaSelect.ClearOptions();
            _areaSelect.AddOptions(areaList.ToList());
            _areaSelect.value = index;
        }

        #region -- Button Events ---

            public void InitEngine()
        {
            var text = this._areaSelect.captionText.text;
            AREA_CODE areaCode = (AREA_CODE)Enum.Parse(typeof(AREA_CODE), text);
            this.Log.UpdateLog("Select AREA_CODE : " + areaCode);

            UserEventHandler handler = new UserEventHandler(this);
            RtcEngineContext context = new RtcEngineContext
            {
                appId = _appID,
                channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING,
                audioScenario = AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_DEFAULT,
                areaCode = areaCode
            };
            var result = RtcEngine.Initialize(context);
            this.Log.UpdateLog("Initialize result : " + result);

            RtcEngine.InitEventHandler(handler);
            RtcEngine.EnableAudio();
            RtcEngine.EnableVideo();
            RtcEngine.SetChannelProfile(CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_COMMUNICATION);
            RtcEngine.SetClientRole(CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER);

            // Fix: Use Optional<bool> for ChannelMediaOptions
            var options = new ChannelMediaOptions();
            options.publishCameraTrack.SetValue(false);      // Disable default camera
            options.publishCustomVideoTrack.SetValue(true);  // Enable custom video feed
            RtcEngine.UpdateChannelMediaOptions(options);
        }

        public void JoinChannel()
        {
            RtcEngine.JoinChannel(_token, _channelName, "", 0);
            var node = MakeVideoView(0);
            CreateLocalVideoCallQualityPanel(node);
        }

        public void LeaveChannel()
        {
            RtcEngine.LeaveChannel();
        }

        public void StartPreview()
        {
            RtcEngine.StartPreview();
            var node = MakeVideoView(0);
            CreateLocalVideoCallQualityPanel(node);
        }

        public void StopPreview()
        {
            DestroyVideoView(0);
            RtcEngine.StopPreview();
        }

        public void StartPublish()
        {
            var options = new ChannelMediaOptions();
            options.publishMicrophoneTrack.SetValue(true);
            options.publishCustomVideoTrack.SetValue(true);  // Use custom video track
            var nRet = RtcEngine.UpdateChannelMediaOptions(options);
            this.Log.UpdateLog("UpdateChannelMediaOptions: " + nRet);
        }

        public void StopPublish()
        {
            var options = new ChannelMediaOptions();
            options.publishMicrophoneTrack.SetValue(false);
            options.publishCustomVideoTrack.SetValue(false);  // Stop custom video track
            var nRet = RtcEngine.UpdateChannelMediaOptions(options);
            this.Log.UpdateLog("UpdateChannelMediaOptions: " + nRet);
        }

        public void AdjustVideoEncodedConfiguration640()
        {
            VideoEncoderConfiguration config = new VideoEncoderConfiguration
            {
                dimensions = new VideoDimensions(640, 360),
                frameRate = 15,
                bitrate = 0
            };
            RtcEngine.SetVideoEncoderConfiguration(config);
        }

        public void AdjustVideoEncodedConfiguration480()
        {
            VideoEncoderConfiguration config = new VideoEncoderConfiguration
            {
                dimensions = new VideoDimensions(480, 480),
                frameRate = 15,
                bitrate = 0
            };
            RtcEngine.SetVideoEncoderConfiguration(config);
        }

        public void GetVideoDeviceManager()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            _videoDeviceSelect.ClearOptions();
            _videoDeviceManager = RtcEngine.GetVideoDeviceManager();
            _videoDeviceInfos = _videoDeviceManager.EnumerateVideoDevices();
            Log.UpdateLog(string.Format("VideoDeviceManager count: {0}", _videoDeviceInfos.Length));
            for (var i = 0; i < _videoDeviceInfos.Length; i++)
            {
                Log.UpdateLog(string.Format("VideoDeviceManager device index: {0}, name: {1}, id: {2}", i,
                    _videoDeviceInfos[i].deviceName, _videoDeviceInfos[i].deviceId));
            }
            _videoDeviceSelect.AddOptions(_videoDeviceInfos.Select(w =>
                    new Dropdown.OptionData(string.Format("{0} :{1}", w.deviceName, w.deviceId))).ToList());
#endif
        }

        public void SelectVideoCaptureDevice()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (_videoDeviceSelect == null) return;
            var option = _videoDeviceSelect.options[_videoDeviceSelect.value].text;
            if (string.IsNullOrEmpty(option)) return;

            var deviceId = option.Split(":".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)[1];
            var ret = _videoDeviceManager.SetDevice(deviceId);
            Log.UpdateLog("SelectVideoCaptureDevice ret:" + ret + " , DeviceId: " + deviceId);

            // Restart webcam with new device
            if (_videoTexture != null)
            {
                _videoTexture.Stop();
                Destroy(_textureBuffer);
            }
            _videoTexture = new WebCamTexture(deviceId);
            _videoTexture.Play();
            _textureBuffer = new Texture2D(_videoTexture.width, _videoTexture.height, TextureFormat.RGBA32, false);
            m_image.texture = _videoTexture;
#endif
        }

        #endregion

        private void OnDestroy()
        {
            Debug.Log("OnDestroy");
            if (RtcEngine != null)
            {
                RtcEngine.InitEventHandler(null);
                RtcEngine.LeaveChannel();
                RtcEngine.Dispose();
            }
            if (_videoTexture != null)
            {
                _videoTexture.Stop();
            }
        }

        internal string GetChannelName()
        {
            return _channelName;
        }

        #region -- Video Render UI Logic ---

        internal static GameObject MakeVideoView(uint uid, string channelId = "")
        {
            var go = GameObject.Find(uid.ToString());
            if (!ReferenceEquals(go, null))
            {
                return go; // reuse
            }

            var videoSurface = MakeImageSurface(uid.ToString());
            if (ReferenceEquals(videoSurface, null)) return null;

            if (uid == 0)
            {
                videoSurface.SetForUser(uid, channelId);
            }
            else
            {
                videoSurface.SetForUser(uid, channelId, VIDEO_SOURCE_TYPE.VIDEO_SOURCE_REMOTE);
            }

            videoSurface.OnTextureSizeModify += (int width, int height) =>
            {
                var transform = videoSurface.GetComponent<RectTransform>();
                if (transform)
                {
                    transform.sizeDelta = new Vector2(width / 2, height / 2);
                    transform.localScale = Vector3.one;
                }
                Debug.Log("OnTextureSizeModify: " + width + "  " + height);
            };

            videoSurface.SetEnable(true);
            return videoSurface.gameObject;
        }

        private static VideoSurface MakeImageSurface(string goName)
        {
            GameObject go = new GameObject();
            if (go == null) return null;

            go.name = goName;
            go.AddComponent<RawImage>();
            go.AddComponent<UIElementDrag>();
            var canvas = GameObject.Find("VideoCanvas");
            if (canvas != null)
            {
                go.transform.parent = canvas.transform;
            }

            go.transform.Rotate(0f, 0.0f, 180.0f);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = new Vector3(2f, 3f, 1f);

            var videoSurface = go.AddComponent<VideoSurface>();
            return videoSurface;
        }

        internal static void DestroyVideoView(uint uid)
        {
            var go = GameObject.Find(uid.ToString());
            if (!ReferenceEquals(go, null))
            {
                Destroy(go);
            }
        }

        #endregion

        public void CreateLocalVideoCallQualityPanel(GameObject parent)
        {
            if (parent.GetComponentInChildren<LocalVideoCallQualityPanel>() != null)
                return;

            var panel = GameObject.Instantiate(this._videoQualityItemPrefab, parent.transform);
            panel.AddComponent<LocalVideoCallQualityPanel>();
        }

        public LocalVideoCallQualityPanel GetLocalVideoCallQualityPanel()
        {
            var go = GameObject.Find("0");
            return go.GetComponentInChildren<LocalVideoCallQualityPanel>();
        }

        public void CreateRemoteVideoCallQualityPanel(GameObject parent, uint uid)
        {
            if (parent.GetComponentInChildren<RemoteVideoCallQualityPanel>() != null)
                return;

            var panel = GameObject.Instantiate(this._videoQualityItemPrefab, parent.transform);
            var comp = panel.AddComponent<RemoteVideoCallQualityPanel>();
            comp.Uid = uid;
        }

        public RemoteVideoCallQualityPanel GetRemoteVideoCallQualityPanel(uint uid)
        {
            var go = GameObject.Find(uid.ToString());
            return go.GetComponentInChildren<RemoteVideoCallQualityPanel>();
        }
    }

    #region -- Agora Event ---

    internal class UserEventHandler : IRtcEngineEventHandler
    {
        private readonly JoinChannelVideo _videoSample;

        internal UserEventHandler(JoinChannelVideo videoSample)
        {
            _videoSample = videoSample;
        }

        public override void OnError(int err, string msg)
        {
            _videoSample.Log.UpdateLog(string.Format("OnError err: {0}, msg: {1}", err, msg));
        }

        public override void OnJoinChannelSuccess(RtcConnection connection, int elapsed)
        {
            int build = 0;
            Debug.Log("Agora: OnJoinChannelSuccess ");
            _videoSample.Log.UpdateLog(string.Format("sdk version: ${0}", _videoSample.RtcEngine.GetVersion(ref build)));
            _videoSample.Log.UpdateLog(string.Format("sdk build: ${0}", build));
            _videoSample.Log.UpdateLog(string.Format("OnJoinChannelSuccess channelName: {0}, uid: {1}, elapsed: {2}",
                                connection.channelId, connection.localUid, elapsed));
        }

        public override void OnRejoinChannelSuccess(RtcConnection connection, int elapsed)
        {
            _videoSample.Log.UpdateLog("OnRejoinChannelSuccess");
        }

        public override void OnLeaveChannel(RtcConnection connection, RtcStats stats)
        {
            _videoSample.Log.UpdateLog("OnLeaveChannel");
        }

        public override void OnClientRoleChanged(RtcConnection connection, CLIENT_ROLE_TYPE oldRole, CLIENT_ROLE_TYPE newRole, ClientRoleOptions newRoleOptions)
        {
            _videoSample.Log.UpdateLog("OnClientRoleChanged");
        }

        public override void OnUserJoined(RtcConnection connection, uint uid, int elapsed)
        {
            _videoSample.Log.UpdateLog(string.Format("OnUserJoined uid: ${0} elapsed: ${1}", uid, elapsed));
            var node = JoinChannelVideo.MakeVideoView(uid, _videoSample.GetChannelName());
            _videoSample.CreateRemoteVideoCallQualityPanel(node, uid);
        }

        public override void OnUserOffline(RtcConnection connection, uint uid, USER_OFFLINE_REASON_TYPE reason)
        {
            _videoSample.Log.UpdateLog(string.Format("OnUserOffLine uid: ${0}, reason: ${1}", uid, (int)reason));
            JoinChannelVideo.DestroyVideoView(uid);
        }

        public override void OnRtcStats(RtcConnection connection, RtcStats stats)
        {
            var panel = _videoSample.GetLocalVideoCallQualityPanel();
            if (panel != null)
            {
                panel.Stats = stats;
                panel.RefreshPanel();
            }
        }

        public override void OnLocalAudioStats(RtcConnection connection, LocalAudioStats stats)
        {
            var panel = _videoSample.GetLocalVideoCallQualityPanel();
            if (panel != null)
            {
                panel.AudioStats = stats;
                panel.RefreshPanel();
            }
        }

        public override void OnLocalVideoStats(RtcConnection connection, LocalVideoStats stats)
        {
            var panel = _videoSample.GetLocalVideoCallQualityPanel();
            if (panel != null)
            {
                panel.VideoStats = stats;
                panel.RefreshPanel();
            }
        }

        public override void OnRemoteVideoStats(RtcConnection connection, RemoteVideoStats stats)
        {
            var panel = _videoSample.GetRemoteVideoCallQualityPanel(stats.uid);
            if (panel != null)
            {
                panel.VideoStats = stats;
                panel.RefreshPanel();
            }
        }
    }

    #endregion
}