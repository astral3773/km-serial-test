using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace KM_Serial_Test
{
    public partial class Form1 : Form
    {
        private SerialPort _serialPort;
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;
        private readonly Timer _pollTimer = new Timer { Interval = 500 };

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            cmbBaud.Items.AddRange(new object[] { "300", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
            cmbBaud.SelectedItem = "115200";
            cmbPort.DropDown += CmbPort_DropDown;
            _pollTimer.Tick += PollTimer_Tick;
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            string selected = cmbPort.SelectedItem?.ToString();
            cmbPort.Items.Clear();
            cmbPort.Items.AddRange(SerialPort.GetPortNames());

            if (selected != null && cmbPort.Items.Contains(selected))
                cmbPort.SelectedItem = selected;
            else if (cmbPort.Items.Count > 0)
                cmbPort.SelectedIndex = 0;
        }

        private void CmbPort_DropDown(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
                Disconnect(false);
            else
                Connect();
        }

        private void Connect()
        {
            if (cmbPort.SelectedItem == null || cmbBaud.SelectedItem == null)
            {
                SetStatus("No Port Selected", Color.OrangeRed);
                return;
            }

            try
            {
                _serialPort = new SerialPort(cmbPort.SelectedItem.ToString(), int.Parse(cmbBaud.SelectedItem.ToString()))
                {
                    NewLine = "\r\n"
                };

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                _serialPort.Open();

                SetStatus("Connected", Color.LimeGreen);
                btnConnect.Text = "Disconnect";
                txtCommand.Enabled = true;
                cmbPort.Enabled = false;
                cmbBaud.Enabled = false;

                AppendOutput($"[Connected to {cmbPort.SelectedItem} @ {cmbBaud.SelectedItem} baud]", Color.Cyan);
                _pollTimer.Start();
            }
            catch (Exception ex)
            {
                SetStatus("Error", Color.OrangeRed);
                AppendOutput($"[Error: {ex.Message}]", Color.OrangeRed);
                _serialPort?.Dispose();
                _serialPort = null;
            }
        }

        private void Disconnect(bool unexpected)
        {
            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                    if (_serialPort.IsOpen)
                        _serialPort.Close();
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch { }

            _pollTimer.Stop();
            btnConnect.Text = "Connect";
            txtCommand.Enabled = false;
            cmbPort.Enabled = true;
            cmbBaud.Enabled = true;

            if (unexpected)
            {
                SetStatus("Device Lost", Color.OrangeRed);
                AppendOutput("[Device disconnected unexpectedly]", Color.OrangeRed);
            }
            else
            {
                SetStatus("Disconnected", Color.Gray);
                AppendOutput("[Disconnected]", Color.Cyan);
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (_serialPort == null) return;

            if (!Array.Exists(SerialPort.GetPortNames(), p => p == _serialPort.PortName))
                Disconnect(true);
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _serialPort?.ReadExisting();
                if (string.IsNullOrEmpty(data)) return;

                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => AppendOutput($"{data.TrimEnd()}", Color.LimeGreen)));
            }
            catch
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => Disconnect(true)));
            }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() => Disconnect(true)));
        }

        private void txtCommand_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SendCommand(txtCommand.Text);
                txtCommand.Clear();
                _historyIndex = -1;
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.SuppressKeyPress = true;
                if (_commandHistory.Count == 0) return;
                _historyIndex = Math.Min(_historyIndex + 1, _commandHistory.Count - 1);
                txtCommand.Text = _commandHistory[_historyIndex];
                txtCommand.SelectionStart = txtCommand.Text.Length;
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                _historyIndex = Math.Max(_historyIndex - 1, -1);
                txtCommand.Text = _historyIndex == -1 ? string.Empty : _commandHistory[_historyIndex];
                txtCommand.SelectionStart = txtCommand.Text.Length;
            }
        }

        private void SendCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            if (command.Equals("cls", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                rtbOutput.Clear();
                return;
            }

            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                _serialPort.WriteLine(command);
                AppendOutput($"<< {command}", Color.Yellow);

                if (_commandHistory.Count == 0 || _commandHistory[0] != command)
                    _commandHistory.Insert(0, command);
            }
            catch
            {
                Disconnect(true);
            }
        }

        private void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private void AppendOutput(string text, Color color)
        {
            rtbOutput.SelectionStart = rtbOutput.TextLength;
            rtbOutput.SelectionLength = 0;
            rtbOutput.SelectionColor = color;
            rtbOutput.AppendText(text + Environment.NewLine);
            rtbOutput.ScrollToCaret();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();

            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                try { if (_serialPort.IsOpen) _serialPort.Close(); } catch { }
                _serialPort.Dispose();
            }
        }
    }
}
