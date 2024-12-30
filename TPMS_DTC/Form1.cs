using NationalInstruments.Analysis;
using NationalInstruments.Analysis.Conversion;
using NationalInstruments.Analysis.Dsp;
using NationalInstruments.Analysis.Dsp.Filters;
using NationalInstruments.Analysis.Math;
using NationalInstruments.Analysis.Monitoring;
using NationalInstruments.Analysis.SignalGeneration;
using NationalInstruments.Analysis.SpectralMeasurements;
using NationalInstruments;
using NationalInstruments.UI;
using NationalInstruments.DAQmx;
using NationalInstruments.NI4882;
using NationalInstruments.VisaNS;
using NationalInstruments.NetworkVariable;
using NationalInstruments.NetworkVariable.WindowsForms;
using NationalInstruments.Tdms;
using NationalInstruments.UI.WindowsForms;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;           // 파일 입출력
using System.Linq;
using System.Text;
using System.Threading;   // System.Threading.Timer
using System.Windows.Forms;

using Peak.Can.Basic;
using TPCANHandle = System.UInt16;
using TPCANBitrateFD = System.String;
using TPCANTimestampFD = System.UInt64;

namespace TPMS_DTC
{
    public partial class TPMS : Form
    {
        // PCAN Handle (채널 1, 채널 2)
        private TPCANHandle m_PcanHandleCH1 = PCANBasic.PCAN_USBBUS1;
        private TPCANHandle m_PcanHandleCH2 = PCANBasic.PCAN_USBBUS2;

        // 채널 연결 여부 표시
        private bool ch1Connected = false;
        private bool ch2Connected = false;

        // ========= [ 로그 관리 ] =========
        private Queue<LogEntry> logQueue = new Queue<LogEntry>(); // RX, TX 로그를 담을 큐

        // 수신 (Read) 타이머 : System.Threading.Timer
        private System.Threading.Timer canReadTimer;

        // 로그 저장 타이머 : WinForms Timer
        private System.Windows.Forms.Timer saveLogTimer;

        // 로그 파일 저장 경로/파일명
        private string folderPath = @"C:\Users\kangdohyun\Desktop\세미나\강도현\8주차\LOG";
        private string txLogFile = "TxLog.txt";
        private string rxLogFile = "RxLog.txt";
        private string allLogFile = "AllLog.txt";

        public TPMS()
        {
            InitializeComponent();

            // 폼이 닫힐 때 이벤트 처리(자동으로 채널 해제)
            this.FormClosing += Form1_FormClosing;
        }

        //======================================================================
        //   CH1 버튼 (연결 / 해제)
        //======================================================================
        private void CH1_Button_Click(object sender, EventArgs e)
        {
            if (!ch1Connected)
            {
                // 연결 시도
                TPCANStatus stsResult = PCANBasic.Initialize(
                    m_PcanHandleCH1,
                    TPCANBaudrate.PCAN_BAUD_500K,
                    (TPCANType)0,
                    0,
                    0
                );

                if (stsResult == TPCANStatus.PCAN_ERROR_OK)
                {
                    ch1Connected = true;
                    CH1_Button.Text = "Connected";
                    CH1_Button.BackColor = Color.Green;
                    MessageBox.Show("Channel 1 Connected!");

                    // 수신 타이머 시작 (CH1, CH2 중 하나라도 연결 시)
                    StartTimersIfNeeded();
                }
                else
                {
                    MessageBox.Show("Failed to connect Channel 1: " + stsResult.ToString());
                }
            }
            else
            {
                // 해제
                PCANBasic.Uninitialize(m_PcanHandleCH1);
                ch1Connected = false;

                CH1_Button.Text = "Connect";
                CH1_Button.BackColor = SystemColors.Control;
                MessageBox.Show("Channel 1 Disconnected.");

                // 두 채널 모두 꺼졌으면 타이머 정지
                StopTimersIfNoConnection();
            }
        }

        //======================================================================
        //   CH2 버튼 (연결 / 해제)
        //======================================================================
        private void CH2_Button_Click(object sender, EventArgs e)
        {
            if (!ch2Connected)
            {
                // 연결 시도
                TPCANStatus stsResult = PCANBasic.Initialize(
                    m_PcanHandleCH2,
                    TPCANBaudrate.PCAN_BAUD_500K,
                    (TPCANType)0,
                    0,
                    0
                );

                if (stsResult == TPCANStatus.PCAN_ERROR_OK)
                {
                    ch2Connected = true;
                    CH2_Button.Text = "Connected";
                    CH2_Button.BackColor = Color.Green;
                    MessageBox.Show("Channel 2 Connected!");

                    // 수신 타이머 시작
                    StartTimersIfNeeded();
                }
                else
                {
                    MessageBox.Show("Failed to connect Channel 2: " + stsResult.ToString());
                }
            }
            else
            {
                // 해제
                PCANBasic.Uninitialize(m_PcanHandleCH2);
                ch2Connected = false;

                CH2_Button.Text = "Connect";
                CH2_Button.BackColor = SystemColors.Control;
                MessageBox.Show("Channel 2 Disconnected.");

                // 두 채널 모두 꺼졌으면 타이머 정지
                StopTimersIfNoConnection();
            }
        }

        //======================================================================
        //   연결 시 수신 / 저장 타이머 시작
        //======================================================================
        private void StartTimersIfNeeded()
        {
            // 하나라도 연결되어 있으면
            if (ch1Connected || ch2Connected)
            {
                // 수신 타이머 (System.Threading.Timer)
                if (canReadTimer == null)
                {
                    // 500ms 간격으로 Read
                    canReadTimer = new System.Threading.Timer(CanReadTimer_Tick, null, 0, 500);
                }

                // 로그 저장 타이머 (WinForms Timer)
                if (saveLogTimer == null)
                {
                    saveLogTimer = new System.Windows.Forms.Timer();
                    saveLogTimer.Interval = 500; // 500ms
                    saveLogTimer.Tick += SaveLogTimer_Tick;
                    saveLogTimer.Start();
                }
            }
        }

        private void StopTimersIfNoConnection()
        {
            // 둘 다 false 면
            if (!ch1Connected && !ch2Connected)
            {
                // 수신 타이머 정지
                if (canReadTimer != null)
                {
                    canReadTimer.Dispose();
                    canReadTimer = null;
                }
                // 로그 저장 타이머 정지
                if (saveLogTimer != null)
                {
                    saveLogTimer.Stop();
                    saveLogTimer = null;
                }
            }
        }

        //======================================================================
        //   프로그램 종료(폼 닫기) 시 자동으로 연결 해제
        //======================================================================
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // ch1Connected = true 상태면 해제
            if (ch1Connected)
            {
                PCANBasic.Uninitialize(m_PcanHandleCH1);
                ch1Connected = false;
            }
            // ch2Connected = true 상태면 해제
            if (ch2Connected)
            {
                PCANBasic.Uninitialize(m_PcanHandleCH2);
                ch2Connected = false;
            }

            // 타이머들 정리
            if (canReadTimer != null)
            {
                canReadTimer.Dispose();
                canReadTimer = null;
            }
            if (saveLogTimer != null)
            {
                saveLogTimer.Stop();
                saveLogTimer = null;
            }
        }

        //======================================================================
        //   수신 타이머 콜백 (CH1/CH2 모두 Read 시도)
        //======================================================================
        private void CanReadTimer_Tick(object state)
        {
            if (ch1Connected)
                ReadFromChannel(m_PcanHandleCH1);

            if (ch2Connected)
                ReadFromChannel(m_PcanHandleCH2);
        }

        private void ReadFromChannel(TPCANHandle handle)
        {
            while (true)
            {
                TPCANMsg message;
                TPCANTimestamp timestamp;

                TPCANStatus status = PCANBasic.Read(handle, out message, out timestamp);
                if (status != TPCANStatus.PCAN_ERROR_OK)
                {
                    // 더 이상 읽을 메시지가 없거나 에러 시 break
                    break;
                }

                // 수신 메시지
                DateTime now = DateTime.Now;
                string canIdHex = message.ID.ToString("X3");
                string dataHex = string.Join(" ", message.DATA.Take(message.LEN).Select(b => b.ToString("X2")));

                LogEntry rxEntry = new LogEntry(
                    "RX",
                    now,
                    canIdHex,
                    message.LEN,
                    dataHex,
                    "RxMsg"
                );

                // 큐에 저장
                lock (logQueue)
                {
                    logQueue.Enqueue(rxEntry);
                }

                // LogListBox에 표시
                UpdateDisplay(rxEntry);

                // (선택) ListBox 실시간 표시
                //AddLogItem("RX", "STD or EXT", canIdHex, message.LEN.ToString(), 
                //          "1", now.ToString("HH:mm:ss.fff"), dataHex, "RxMsg");
            }
        }

        //======================================================================
        //   데이터 전송 버튼 (Tx)
        //======================================================================
        private void DataTransmit_Click(object sender, EventArgs e)
        {
            // 어느 채널로 보낼 것인지 결정 (예시로 CH1 우선)
            TPCANHandle handleToUse = PCANBasic.PCAN_NONEBUS;
            if (ch1Connected)
                handleToUse = m_PcanHandleCH1;
            else if (ch2Connected)
                handleToUse = m_PcanHandleCH2;
            else
            {
                MessageBox.Show("No channel is connected!");
                return;
            }

            // dataGridView1에서 현재 선택된 행(SelectedRow) 찾기
            if (dataGridView1.SelectedRows.Count < 1)
            {
                MessageBox.Show("Please select a row in the grid to transmit.");
                return;
            }

            DataGridViewRow selectedRow = dataGridView1.SelectedRows[0];
            // 열에서 CAN ID / DATA / FRAME TYPE 가져오기
            string canIdHex = selectedRow.Cells[0].Value.ToString();
            string dataString = selectedRow.Cells[1].Value.ToString();
            string frameType = selectedRow.Cells[2].Value.ToString();

            // CAN 메시지 만들기
            TPCANMsg canMsg = new TPCANMsg();
            if (frameType == "EXT")
                canMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_EXTENDED;
            else
                canMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            // ID 설정
            try
            {
                canMsg.ID = Convert.ToUInt32(canIdHex, 16);
            }
            catch
            {
                MessageBox.Show("Invalid CAN ID in selected row.");
                return;
            }

            // DATA 파싱
            string[] dataHexArr = dataString.Split(' ');
            byte[] msgData = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    msgData[i] = Convert.ToByte(dataHexArr[i], 16);
                }
                catch
                {
                    msgData[i] = 0x00;
                }
            }
            canMsg.DATA = msgData;
            canMsg.LEN = 8;

            // 전송
            TPCANStatus stsResult = PCANBasic.Write(handleToUse, ref canMsg);
            if (stsResult == TPCANStatus.PCAN_ERROR_OK)
            {
                //MessageBox.Show("Message transmitted successfully.");

                // 전송 성공 -> TX 로그
                DateTime now = DateTime.Now;
                string dataHex = string.Join(" ", msgData.Take(8).Select(b => b.ToString("X2")));

                LogEntry txEntry = new LogEntry(
                    "TX",
                    now,
                    canIdHex,
                    canMsg.LEN,
                    dataHex,
                    "TxMsg"
                );

                // 큐에 넣기
                lock (logQueue)
                {
                    logQueue.Enqueue(txEntry);
                }

                // LogListBox에 표시
                UpdateDisplay(txEntry);
            }
            else
            {
                MessageBox.Show("Transmit failed: " + stsResult.ToString());
            }
        }

        //======================================================================
        //   DataCreate / DataDelete (GridView 행 편집)
        //======================================================================
        private void DataCreate_Click(object sender, EventArgs e)
        {
            // 메시지 ID
            string msgIdHex = MSG_ID_Edit.Text.Trim();
            if (string.IsNullOrEmpty(msgIdHex))
            {
                MessageBox.Show("Please enter Message ID.");
                return;
            }

            // 데이터 바이트
            string[] dataHexArray = new string[8];
            for (int i = 0; i < 8; i++)
            {
                string boxName = "tb_byte" + i.ToString();
                Control[] boxes = this.Controls.Find(boxName, true);
                if (boxes.Length > 0 && boxes[0] is TextBox)
                {
                    TextBox t = (TextBox)boxes[0];
                    string hexVal = t.Text.Trim();
                    if (string.IsNullOrEmpty(hexVal))
                        hexVal = "00";
                    dataHexArray[i] = hexVal;
                }
                else
                {
                    dataHexArray[i] = "00";
                }
            }

            string dataString = string.Join(" ", dataHexArray.Select(s => s.ToUpper()));
            string frameType = "STD"; // 기본

            int rowIndex = dataGridView1.Rows.Add();
            dataGridView1.Rows[rowIndex].Cells[0].Value = msgIdHex.ToUpper();
            dataGridView1.Rows[rowIndex].Cells[1].Value = dataString;
            dataGridView1.Rows[rowIndex].Cells[2].Value = frameType;

            MessageBox.Show("Message Created / Added to Grid.");
        }

        private void DataDelete_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0)
            {
                MessageBox.Show("No row is selected for deletion!");
                return;
            }

            foreach (DataGridViewRow row in dataGridView1.SelectedRows)
            {
                if (!row.IsNewRow)
                    dataGridView1.Rows.Remove(row);
            }
        }

        //======================================================================
        //   FlowControl (FC_Set) 기존 로직 (생략 가능)
        //======================================================================
        private void FC_Set_Click(object sender, EventArgs e)
        {
            TPCANHandle handleToUse = PCANBasic.PCAN_NONEBUS;
            if (ch1Connected)
                handleToUse = m_PcanHandleCH1;
            else if (ch2Connected)
                handleToUse = m_PcanHandleCH2;
            else
            {
                MessageBox.Show("No channel is connected!");
                return;
            }

            TPCANMsg fcMsg = new TPCANMsg();
            fcMsg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            try
            {
                string fcIdHex = FC_ID_Edit.Text.Trim();
                fcMsg.ID = Convert.ToUInt32(fcIdHex, 16);
            }
            catch
            {
                MessageBox.Show("Invalid FC ID (Hex).");
                return;
            }

            byte fs = 0x00;
            if (cb_FS.SelectedIndex >= 0)
                fs = (byte)cb_FS.SelectedIndex;

            byte bs = 0x00;
            try
            {
                bs = Convert.ToByte(tb_bs.Text.Trim(), 16);
            }
            catch
            {
                bs = 0x00;
            }

            byte stMin = 0x00;
            if (rBtn_ms.Checked)
            {
                int stMinValueMs = (int)udStmin_ms.Value;
                stMin = (byte)Math.Min(stMinValueMs, 0x7F);
            }
            else if (rBtn_μs.Checked)
            {
                int stMinValueUs = (int)dud_STmin_us.Value;
                stMin = (byte)Math.Min(stMinValueUs, 0xFF);
            }

            byte[] fcData = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                string boxName = "tb_fc" + i.ToString();
                Control[] boxes = this.Controls.Find(boxName, true);
                if (boxes.Length > 0 && boxes[0] is TextBox)
                {
                    TextBox t = (TextBox)boxes[0];
                    try
                    {
                        fcData[i] = Convert.ToByte(t.Text.Trim(), 16);
                    }
                    catch
                    {
                        fcData[i] = 0x00;
                    }
                }
            }

            fcData[0] = fs;
            fcData[1] = bs;
            fcData[2] = stMin;

            fcMsg.DATA = fcData;
            fcMsg.LEN = 8;

            TPCANStatus result = PCANBasic.Write(handleToUse, ref fcMsg);
            if (result == TPCANStatus.PCAN_ERROR_OK)
            {
                MessageBox.Show("Flow Control Set OK.");
            }
            else
            {
                MessageBox.Show("Flow Control Set failed: " + result.ToString());
            }
        }

        //======================================================================
        //   기타 이벤트 (CheckedChanged 등)
        //======================================================================
        private void cb_FS_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (cb_FS.SelectedIndex)
            {
                case 0:
                    tb_fc0.Text = "30";
                    break;
                case 1:
                    tb_fc0.Text = "31";
                    break;
                case 2:
                    tb_fc0.Text = "32";
                    break;
            }
        }

        private void tb_bs_TextChanged(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(tb_bs.Text))
            {
                if (Convert.ToInt32(tb_bs.Text, 16) > 127)
                {
                    MessageBox.Show("Maximum value of BS is 7F.", "", MessageBoxButtons.OK);
                    tb_bs.Text = "7F";
                }
                else
                {
                    tb_fc1.Text = string.Format(tb_bs.Text, "X2");
                }
            }
        }

        private void udStmin_ms_ValueChanged(object sender, EventArgs e)
        {
            tb_fc2.Text = Convert.ToInt32(udStmin_ms.Value).ToString("X2");
        }

        private void dud_STmin_us_ValueChanged(object sender, EventArgs e)
        {
            if (dud_STmin_us.Value % 100 == 0)
            {
                if (dud_STmin_us.Value == 0)
                    tb_fc2.Text = "00";
                else
                    tb_fc2.Text = string.Format("F{0}", dud_STmin_us.Value / 100);
            }
            else
            {
                MessageBox.Show("It can only be set in 100 units", "", MessageBoxButtons.OK);
                dud_STmin_us.Value = 0;
            }
        }

        private void rBtn_ms_CheckedChanged(object sender, EventArgs e)
        {
            if (rBtn_ms.Checked)
            {
                udStmin_ms.Enabled = true;
                dud_STmin_us.Enabled = false;
            }
        }

        private void rBtn_μs_CheckedChanged(object sender, EventArgs e)
        {
            if (rBtn_μs.Checked)
            {
                udStmin_ms.Enabled = false;
                dud_STmin_us.Enabled = true;
            }
        }

        //======================================================================
        //   로그를 ListBox에 표시하기 (LogListBox)
        //======================================================================

        private void UpdateDisplay(string logEntry)
        {
            if (LogListBox.InvokeRequired)
            {
                LogListBox.Invoke(new System.Action(() =>
                {
                    AddLogToListBox(logEntry);
                }));
            }
            else
            {
                AddLogToListBox(logEntry);
            }
        }

        private void AddLogToListBox(string logEntry)
        {
            // 로그 데이터를 파싱하여 ListBox에 추가
            var logDetails = logEntry.Split('|'); // 로그를 "|" 기준으로 나눔

            if (logDetails.Length >= 5) // 최소한의 데이터가 있는지 확인
            {
                // 데이터 추출
                string timestamp = logDetails[0].Trim();      // Time
                string direction = logDetails[1].Trim();     // TxRx
                string canId = logDetails[2].Trim().Replace("ID=", ""); // Id
                string length = logDetails[3].Trim();        // Length
                string data = logDetails[4].Trim();          // Data
                string description = logDetails.Length > 5 ? logDetails[5].Trim() : ""; // Description

                // Type 결정
                string type = GetLogTypeFromData(data);

                // Rx인 경우 ID가 7DE여야만 표시
                if (direction == "RX" && canId != "7DE")
                {
                    return; // Rx가 아니면 추가하지 않음
                }

                // LogListBox에 추가할 텍스트 생성
                string logText = string.Format("{0} | {1} | {2} | ID={3} | Len={4} | Data={5} | {6}",
                    timestamp, direction, type, canId, length, data, description);

                // LogListBox에 추가
                LogListBox.Items.Add(logText);

                // 표시 항목 수 제한 (예: 100개)
                if (LogListBox.Items.Count > 100)
                {
                    LogListBox.Items.RemoveAt(0); // 가장 오래된 항목 제거
                }

                // 마지막 항목으로 스크롤
                LogListBox.TopIndex = LogListBox.Items.Count - 1;
            }
        }

        // Type 결정 메서드 (데이터에서 tb_byte01 값 추출)
        private string GetLogTypeFromData(string dataHex)
        {
            // 데이터 문자열을 배열로 변환
            var dataBytes = dataHex.Split(' ')
                                   .Select(b => Convert.ToByte(b, 16))
                                   .ToArray();

            // tb_byte01 값에 따라 Type 결정
            if (dataBytes.Length > 1)
            {
                switch (dataBytes[1]) // tb_byte01에 해당
                {
                    case 0x81:
                        return "Standard";
                    case 0x85:
                        return "EcuProgramming";
                    case 0x90:
                        return "Extended";
                    default:
                        return "Unknown";
                }
            }
            return "Unknown";
        }

        //======================================================================
        //   로그 저장 타이머 : 500ms마다 logQueue → 파일 쓰기
        //======================================================================
        private void SaveLogTimer_Tick(object sender, EventArgs e)
        {
            List<LogEntry> logsToWrite = new List<LogEntry>();

            // 큐에서 모두 빼기
            lock (logQueue)
            {
                while (logQueue.Count > 0)
                {
                    logsToWrite.Add(logQueue.Dequeue());
                }
            }
            if (logsToWrite.Count == 0) return;

            // 폴더 없으면 생성
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string txPath = Path.Combine(folderPath, txLogFile);
            string rxPath = Path.Combine(folderPath, rxLogFile);
            string allPath = Path.Combine(folderPath, allLogFile);

            // 세 파일을 한 번에 open
            using (StreamWriter swTx = new StreamWriter(txPath, true))
            using (StreamWriter swRx = new StreamWriter(rxPath, true))
            using (StreamWriter swAll = new StreamWriter(allPath, true))
            {
                foreach (var entry in logsToWrite)
                {
                    string line = entry.ToString();

                    // All은 무조건 기록
                    swAll.WriteLine(line);

                    // TX만
                    if (entry.Direction == "TX")
                    {
                        swTx.WriteLine(line);
                    }
                    // RX만
                    else if (entry.Direction == "RX")
                    {
                        swRx.WriteLine(line);
                    }
                }
            }
        }

        // LogListBox에 Display
        private void UpdateDisplay(LogEntry logEntry)
        {
            if (LogListBox.InvokeRequired)
            {
                LogListBox.Invoke(new System.Action(() =>
                {
                    AddLogToListBox(logEntry);
                }));
            }
            else
            {
                AddLogToListBox(logEntry);
            }
        }

        private void AddLogToListBox(LogEntry logEntry)
        {
            // Rx의 경우 ID가 "7DE"일 때만 표시
            if (logEntry.Direction == "RX" && logEntry.CanIdHex != "7DE")
            {
                return;
            }

            // 로그 텍스트 형식화
            string logText = logEntry.ToString(); // LogEntry의 ToString() 메서드 활용

            // LogListBox에 추가
            LogListBox.Items.Add(logText);

            // 표시 항목 수 제한 (예: 100개)
            if (LogListBox.Items.Count > 100)
            {
                LogListBox.Items.RemoveAt(0); // 가장 오래된 항목 제거
            }

            // 마지막 항목으로 스크롤
            LogListBox.TopIndex = LogListBox.Items.Count - 1;
        }

        // 명령어 즐겨찾기
        private void ServiceList_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TreeNode selectedNode = e.Node;
            // 선택된 노드 이름에 따라 동작을 정의합니다.
            switch (selectedNode.Name)
            {
                 //////////////////////////////////////
                // === Standard Diagnostic Mode === //
               //////////////////////////////////////

                case "nodeStandard":
                    MessageBox.Show("Standard Diagnostic Mode 선택됨");
                    break;

                case "StartDiagnostic":
                    SendCanCommand(new byte[] { 0x10, 0x81 });
                    break;

                case "StopDiagnostic":
                    SendCanCommand(new byte[] { 0x20 });
                    break;

                // === Read ECU Identification ID ===
                case "VehicleProject":
                    SendCanCommand(new byte[] { 0x1A, 0x91 });
                    break;

                case "EcuIdentification":
                    SendCanCommand(new byte[] { 0x1A, 0x80 });
                    break;

                case "HMC/KMC":
                    SendCanCommand(new byte[] { 0x1A, 0x86 });
                    break;

                case "VIN":
                    SendCanCommand(new byte[] { 0x1A, 0x90 });
                    break;

                case "ReadSensors":
                    SendCanCommand(new byte[] { 0x1A, 0x8B });
                    break;

                case "ManufacturerPart":
                    SendCanCommand(new byte[] { 0x1A, 0x87 });
                    break;

                // === Read DTC By Status ===
                case "ActiveFault":
                    SendCanCommand(new byte[] { 0x18, 0x00, 0x40, 0x00 });
                    break;

                case "HistoricFault":
                    SendCanCommand(new byte[] { 0x18, 0x01, 0x40, 0x00 });
                    break;

                // === Clear Diagnostic Information ===
                case "ClearAll":
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x00 });
                    break;

                case "ActiveDTCS":
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x01 });
                    break;

                case "HistoricDTCS":
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x02 });
                    break;

                 ////////////////////////////////////
                /// === ECU Programming Mode === ///
               ////////////////////////////////////

                case "nodeECUProgrammingMode":
                    MessageBox.Show("ECU Programming Mode 선택됨");
                    break;

                case "StartDiagnostic2":
                    SendCanCommand(new byte[] { 0x10, 0x85 });
                    break;

                // === Read Data By Local Identifier ===
                case "ECUInputBattery":
                    SendCanCommand(new byte[] { 0x21, 0x01 });
                    break;

                case "LampDrive":
                    SendCanCommand(new byte[] { 0x21, 0x02 });
                    break;

                case "SensorStatus":
                    SendCanCommand(new byte[] { 0x21, 0x06 });
                    break;

                case "EcuStatus":
                    SendCanCommand(new byte[] { 0x21, 0xAF });
                    break;

                // === Write Data By Local Identifier ===
                case "VehicleProject&WheelSize":
                    SendCanCommand(new byte[] { 0x3B, 0x91 });
                    break;

                case "EcuIdentificationData":
                    SendCanCommand(new byte[] { 0x3B, 0x80 });
                    break;

                case "HMC/KMCData":
                    SendCanCommand(new byte[] { 0x3B, 0x86 });
                    break;

                case "VINData":
                    SendCanCommand(new byte[] { 0x3B, 0x90 });
                    break;

                case "SensorIDType":
                    SendCanCommand(new byte[] { 0x3B, 0x8B });
                    break;

                case "ManufacturePartInfo":
                    SendCanCommand(new byte[] { 0x3B, 0x87 });
                    break;

                //////////////////////////////////////
                // === Standard Diagnostic Mode === //
                //////////////////////////////////////

                case "nodeExtended":
                    MessageBox.Show("Extended Diagnostic Mode 선택됨");
                    break;

                case "StartDiagnostic3":
                    SendCanCommand(new byte[] { 0x10, 0x90 });
                    break;

                case "StopDiagnostic2":
                    SendCanCommand(new byte[] { 0x20 });
                    break;

                // === Read ECU Identification ID ===
                case "VehicleProject2":
                    SendCanCommand(new byte[] { 0x1A, 0x91 });
                    break;

                case "EcuIdentification2":
                    SendCanCommand(new byte[] { 0x1A, 0x80 });
                    break;

                case "HMC/KMC2":
                    SendCanCommand(new byte[] { 0x1A, 0x86 });
                    break;

                case "VIN2":
                    SendCanCommand(new byte[] { 0x1A, 0x90 });
                    break;

                case "ReadSensors2":
                    SendCanCommand(new byte[] { 0x1A, 0x8B });
                    break;

                case "ManufacturerPart2":
                    SendCanCommand(new byte[] { 0x1A, 0x87 });
                    break;

                default:
                    MessageBox.Show("알 수 없는 노드 선택됨: {0}", selectedNode.Text);
                    break;
            }
        }

        // CAN 명령 데이터를 tb_byte0 ~ tb_byte7에 적용하는 함수
        private void SendCanCommand(byte[] command)
        {
            // 명령 데이터가 7바이트 초과인 경우 에러 처리 (tb_byte1 ~ tb_byte7 사용 가능)
            /*
            if (command.Length > 7)
            {
                MessageBox.Show("데이터는 최대 7바이트까지 허용됩니다. (tb_byte1 ~ tb_byte7)");
                return;
            }*/

            // tb_byte0에 데이터 길이 적용
            Control[] box0 = this.Controls.Find("tb_byte0", true); // tb_byte0 찾기
            if (box0.Length > 0 && box0[0] is TextBox)
            {
                TextBox textBox0 = (TextBox)box0[0];
                textBox0.Text = string.Format("{0:X2}", command.Length); // 데이터 길이를 16진수로 설정
            }

            // tb_byte1 ~ tb_byte7 TextBox 컨트롤에 명령 데이터 적용
            for (int i = 0; i < 7; i++) // 최대 7바이트 적용 가능
            {
                string boxName = string.Format("tb_byte{0}", i + 1); // TextBox 이름 생성 (tb_byte1부터 시작)
                Control[] boxes = this.Controls.Find(boxName, true); // TextBox 컨트롤 찾기

                if (boxes.Length > 0 && boxes[0] is TextBox) // TextBox가 존재하고 올바른 타입인지 확인
                {
                    TextBox textBox = (TextBox)boxes[0]; // TextBox로 캐스팅
                    if (i < command.Length)
                    {
                        // 명령 데이터 적용
                        textBox.Text = string.Format("{0:X2}", command[i]);
                    }
                    else
                    {
                        // 나머지는 00으로 채우기
                        textBox.Text = "00";
                    }
                }
            }

            // 명령 데이터 적용 결과를 메시지로 출력
            string[] commandHexArray = new string[command.Length];
            for (int i = 0; i < command.Length; i++)
            {
                commandHexArray[i] = string.Format("0x{0:X2}", command[i]);
            }

            string commandString = string.Join(" ", commandHexArray);
            MessageBox.Show(string.Format("tb_byte0 ~ tb_byte7에 데이터가 적용되었습니다. tb_byte0: {0:X2}, 데이터: {1}", command.Length, commandString));
        }

        // 로그를 담을 간단한 클래스
        public class LogEntry
        {
            public string Direction;   // "TX" or "RX"

            public DateTime Timestamp;
            public string CanIdHex;    // 예: "7D6"
            public int DataLength;     // 0~8
            public string DataHex;     // 예: "00 11 22 33 ..."
            public string Description; // 추가 설명

            public LogEntry(string dir, DateTime ts, string idHex, int len, string data, string desc = "")
            {
                Direction = dir;
                Timestamp = ts;
                CanIdHex = idHex;
                DataLength = len;
                DataHex = data;
                Description = desc;
            }

            public override string ToString()
            {
                // Type 결정 (tb_byte01 값으로 결정)
                string type = GetLogTypeFromData(DataHex);

                // 예) 2023-10-12 14:08:32.123 | TX | Standard | ID=7B7 | Len=8 | Data=00 11 22 33 44 55 66 77
                return string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2} | ID={3} | Len={4} | Data={5} | {6}",
                    Timestamp, Direction, type, CanIdHex, DataLength, DataHex, Description);
            }

            // Type 결정 메서드 (데이터에서 tb_byte01 값 추출)
            private string GetLogTypeFromData(string dataHex)
            {
                // 데이터 문자열을 배열로 변환
                var dataBytes = dataHex.Split(' ')
                                       .Select(b => Convert.ToByte(b, 16))
                                       .ToArray();

                // tb_byte01 값에 따라 Type 결정
                if (dataBytes.Length > 1)
                {
                    switch (dataBytes[2]) // tb_byte01에 해당
                    {
                        case 0x81:
                            return "Standard";
                        case 0x85:
                            return "EcuProgramming";
                        case 0x90:
                            return "Extended";
                        default:
                            return "Unknown";
                    }
                }
                return "Unknown";
            }
        }
    }
}
