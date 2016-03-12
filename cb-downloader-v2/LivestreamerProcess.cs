﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace cb_downloader_v2
{
    internal class LivestreamerProcess
    {
        private static readonly Random Random = new Random();
        private const string StreamTerminatedMessage = "[cli][info] Stream ended";
        private const string StreamInvalidLinkMessagePart = "(404 Client Error: NOT FOUND)";
        private const string StreamOfflineMessagePart = "error: No streams found on this URL: ";
        private const string CommandArguments = "chaturbate.com/{0} {1} -o " + MainForm.OutputFolderName + "/{0}-{2}-{3}.flv";
        private const string Quality = "best";
        private const int RestartDelay = 30 * 1000;
        private readonly MainForm _mf;
        private readonly string _modelName;
        private readonly CancellationTokenSource _cancelToken = new CancellationTokenSource();
        private Process _process;

        public bool IsRunning => _process != null && Started && !_process.HasExited;
        private bool Started { get; set; }
        public bool RestartRequired { get; private set; } = true;
        public bool InvalidUrlDetected { get; private set; }
        public int RestartTime { get; private set; }

        public LivestreamerProcess(MainForm parent, string modelName)
        {
            _mf = parent;
            _modelName = modelName;
        }

        public void Start()
        {
            // Checking if a process is not already running
            if (_process != null)
                return;

            // Initialising process
            DateTime timeNow = MainForm.TimeNow;
            // XXX still need to improve file clash mechanism?

            _process = new Process
            {
                StartInfo =
                {
                    FileName = "livestreamer.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    Arguments = string.Format(CommandArguments, _modelName, Quality, timeNow.ToString("ddMMyy"), timeNow.ToString("hhmmss"))
                }
            };
            
            // Delayed start, to prevent massive cpu spikes
            Task.Delay(Random.Next(500, 30000), _cancelToken.Token).ContinueWith((task =>
            {
#if DEBUG
                Debug.WriteLine("Started #" + _modelName);
#endif

                // Updating flags and starting process
                RestartRequired = false;
                InvalidUrlDetected = false;
                _process.OutputDataReceived += _process_OutputDataReceived;
                _process.Start();
                _process.BeginOutputReadLine();
                Started = true;
                _mf.SetCheckState(_modelName, CheckState.Checked);
            }), _cancelToken.Token);
        }

        private void _process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            string line = e.Data;

            // Checking line validity
            if (string.IsNullOrEmpty(line))
                return;

            // Checking if the stream was terminated
            if (line.Equals(StreamTerminatedMessage))
            {
#if DEBUG
                Debug.WriteLine("[" + _modelName + "] Terminated");
#endif

                // Terminating the thread and marking for a restart
                RestartRequired = true;
                RestartTime = 0;
                Terminate();
                _mf.SetCheckState(_modelName, CheckState.Unchecked);
            }

            // Checking if the username is invalid
            if (line.Contains(StreamInvalidLinkMessagePart))
            {
#if DEBUG
                Debug.WriteLine("[" + _modelName + "] Not Found (404)");
#endif

                // Terminating the thread and marking as invalid url
                InvalidUrlDetected = true;
                _mf.SetCheckState(_modelName, CheckState.Unchecked);
                Terminate();
            }

            // Checking if stream is offline
            if (line.StartsWith(StreamOfflineMessagePart))
            {
#if DEBUG
                Debug.WriteLine("[" + _modelName + "] is Offline");
#endif

                // Terminating the thread and marking for a delayed restart
                RestartRequired = true;
                RestartTime = Environment.TickCount + RestartDelay;
                _mf.SetCheckState(_modelName, CheckState.Unchecked);
                Terminate();
            }
        }

        public void Terminate()
        {
            // Checking if process is existent
            if (_process == null)
                return;

            // Set the cancellation token
            _cancelToken.Cancel();

            // Attempting to close process
            if (IsRunning)
            {
                _process.Kill();
                _process.Close();
            }
            _process = null;
        }
    }
}
