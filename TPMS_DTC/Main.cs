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

        // 현재 모드 상태를 저장할 변수
        private string currentType1 = "Unknown";

        // 수신 메시지를 처리하기 위한 변수 선언
        private List<byte> receivedData = new List<byte>();
        private List<byte> receivedData2 = new List<byte>();
        private int expectedDataLength = 0; // 전체 데이터 길이

        //Tx 메세지의 Description을 처리하기 위한 변수 선언
        private string data_description = "Unkwnon";

        // 이전 시간과 현재 시간 기록을 위한 변수
        private double previousTimestamp = 0.0; // 이전 타임스탬프
        private int count = 0; // Count 값

        //MultiFrame의 데이터 파싱
        //private string parsedData; 

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

        //===========================================================================
        //   수신 타이머 콜백 (CH1/CH2 모두 Read 시도) + 수신 메시지 에러 처리 + FC 송신
        //===========================================================================
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
                string type = GetLogTypeFromData(dataHex); // Type 정의
                string description = "RxMsg"; // 기본 설명

                // 수신 메시지 처리
                if (message.DATA[0] >> 4 == 0x10) // First Frame (FF)
                {
                    expectedDataLength = ((message.DATA[0] & 0x0F) << 8) | message.DATA[1];
                    receivedData = message.DATA.Skip(2).ToList();

                    // FC 전송 준비
                    SendFlowControl(handle, blockSize: 0, stMin: 5);
                    
                    description = "First Frame : " + data_description;
                    type = "MultiFrame";
                }
                else if (message.DATA[0] >= 0x21 && message.DATA[0] < 0x30) // Consecutive Frame (CF)
                {
                    int sequenceNumber = message.DATA[0] & 0x0F;
                    receivedData.AddRange(message.DATA.Skip(1));

                    description = string.Format("Consecutive Frame {0} : {1}", sequenceNumber, data_description);
                    type = "MultiFrame";

                    if (receivedData.Count >= expectedDataLength)
                    {
                        receivedData.Clear();
                        expectedDataLength = 0;

                        description = "Complete MultiFrame : " + data_description;
                    }
                    else
                    {
                        // FC 전송 준비
                        SendFlowControl(handle, blockSize: 0, stMin: 5);
                    }
                }
                else if (message.DATA[0] == 0x30)
                {
                    type = "Flow Control";
                    description = "Flow Control RX : " + data_description;
                }
                else if (canIdHex == "7DE" && message.LEN > 1 && message.DATA[1] == 0x7F) // 에러 코드 식별 (0번째 바이트: 01, 1번째 바이트에 따라 결정)
                {
                    switch (message.DATA[3])
                    {
                        case 0x11:
                            description = "Error: ServiceNotSupported";
                            break;
                        case 0x12:
                            description = "Error: SubFunctionNotSupport-invalidFormat";
                            break;
                        case 0x13:
                            description = "Error: IncorrectMessageLengthOrInvalidFormat";
                            break;
                        case 0x21:
                            description = "Error: Busy – repeatRequest";
                            break;
                        case 0x22:
                            description = "Error: conditionsNotCorrect / requenstSequenceError";
                            break;
                        case 0x78:
                            description = "Error: requestCorrectlyReceived-ResponsePending";
                            break;
                        default:
                            description = "Error: Unknown Error Code";
                            break;
                    }
                }
                else if (canIdHex == "7DE")
                {
                    description = "Response : " + data_description;
                }

                if (canIdHex == "593")
                {
                    description = "There is no Tx";
                    ProcessTimestamp(timestamp);
                }

                LogEntry rxEntry = new LogEntry(
                    "RX",
                    now,
                    type,
                    canIdHex,
                    message.LEN,
                    dataHex,
                    description
                );

                // 큐에 저장
                lock (logQueue)
                {
                    logQueue.Enqueue(rxEntry);
                }

                // LogListBox에 표시
                UpdateDisplay(rxEntry);

                // Rx 데이터 파싱 호출
                ProcessRxData(message, description);
            }
        }

        private void ProcessTimestamp(TPCANTimestamp timestamp)
        {
            // 현재 타임스탬프 계산 (마이크로초 단위로 변환)
            double currentTimestamp = timestamp.millis + (timestamp.micros / 1000.0);

            // Cycle 계산
            if (previousTimestamp != 0.0)
            {
                double cycle = currentTimestamp - previousTimestamp; // 이전 타임스탬프와 현재 타임스탬프의 차이 계산
                UpdateLabelSafely(CycleLav, cycle.ToString("F2")); // 소수점 2자리까지 표시
            }
            else
            {
                UpdateLabelSafely(CycleLav, "0.0"); // 이전 타임스탬프가 없으면 0.0으로 표시
            }

            // 이전 타임스탬프 업데이트
            previousTimestamp = currentTimestamp;

            // Count 증가 및 업데이트
            count++;
            UpdateLabelSafely(CountLav, count.ToString());
        }

        // UI 라벨 업데이트 메서드 (스레드 안전 처리)
        private void UpdateLabelSafely(Label label, string text)
        {
            if (label == CountLav)
            {
                if (label.InvokeRequired)
                {
                    label.Invoke((MethodInvoker)(() => label.Text = "Count : " + text));
                }
                else
                {
                    label.Text = "Count : " + text;
                }
            }
            else if (label == CycleLav)
            {
                if (label.InvokeRequired)
                {
                    label.Invoke((MethodInvoker)(() => label.Text = "Cycle(ms) : " + text));
                }
                else
                {
                    label.Text = "Cycle(ms) : " + text;
                }
            }
        }

        //Rx 에러 메세지 글자 빨간색 처리
        private void LogListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            // 현재 항목 가져오기
            string itemText = LogListBox.Items[e.Index].ToString();

            // 기본 배경과 텍스트 색상 설정
            e.DrawBackground();

            //기본 색상
            Brush textBrush = Brushes.Black;

            // 텍스트 색상 변경 (Description이 "Error"로 시작하는 경우 빨간색)
            if(itemText.Contains("Error"))
            {
                textBrush = Brushes.Red;
            }
            else if(itemText.StartsWith("Read Info"))
            {
                textBrush = Brushes.Blue;
            }
            else
            {
                textBrush = Brushes.Black;
            }

            // 텍스트 색상 변경 (Description이 "Error"로 시작하는 경우 빨간색)
            //Brush textBrush = itemText.Contains("Error") ? Brushes.Red : Brushes.Black;

            // 텍스트 색상 설정 (Decription이 "Additional Info"로 시작하는 경우 파란색)
            //Brush textBrush2 = itemText.StartsWith("Additional Info") ? Brushes.Blue : Brushes.Black;

            // 텍스트 그리기
            e.Graphics.DrawString(itemText, e.Font, textBrush, e.Bounds);

            e.DrawFocusRectangle();
        }

        private void SendFlowControl(uint canId, byte blockSize, byte stMin)
        {
            // FC 메시지 데이터 구성
            byte[] fcData = new byte[8];
            fcData[0] = 0x30; // Flow Control: Continue To Send
            fcData[1] = blockSize; // Block Size (0: 제한 없음)
            fcData[2] = stMin; // Minimum Separation Time
            for (int i = 3; i < 8; i++)
            {
                fcData[i] = 0x00; // 패딩
            }

            // FC 전송 (SendCanMessage 활용)
            SendCanMessage(canId, "Flow Control", fcData, "Flow Control");
        }

        //======================================================================
        //   데이터 전송 버튼 (Tx) + FC 전송 버튼
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
            // 열에서 CAN ID / SID / DATA / FRAME TYPE 가져오기
            string canIdHex = selectedRow.Cells[0].Value.ToString();
            string sid = selectedRow.Cells[1].Value.ToString();
            string dataString = selectedRow.Cells[2].Value.ToString();
            string frameType = selectedRow.Cells[3].Value.ToString();
            string description = selectedRow.Cells[4].Value.ToString();

            // SID + Data 값을 생성
            string fullDataString = sid + " " + dataString;

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
            string[] dataHexArr = fullDataString.Split(' ');
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

            string type = GetLogTypeFromData(fullDataString); // Type 정의

            // 전송
            TPCANStatus stsResult = PCANBasic.Write(handleToUse, ref canMsg);
            if (stsResult == TPCANStatus.PCAN_ERROR_OK)
            {
                // 전송 성공 -> TX 로그
                DateTime now = DateTime.Now;
                string dataHex = string.Join(" ", msgData.Take(8).Select(b => b.ToString("X2")));

                LogEntry txEntry = new LogEntry(
                    "TX",
                    now,
                    type,
                    canIdHex,
                    canMsg.LEN,
                    dataHex,
                    description
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

        private void FcTransmit_Click(object sender, EventArgs e)
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

            // dataGridView2에서 현재 선택된 행(SelectedRow) 찾기
            if (dataGridView2.SelectedRows.Count < 1)
            {
                MessageBox.Show("Please select a row in the grid to transmit.");
                return;
            }

            DataGridViewRow selectedRow = dataGridView2.SelectedRows[0];
            // 열에서 CAN ID / SID / DATA / FRAME TYPE 가져오기
            string canIdHex = selectedRow.Cells["ID2"].Value.ToString();
            string sid = selectedRow.Cells["SID2"].Value.ToString();
            string dataString = selectedRow.Cells["Data2"].Value.ToString();
            string frameType = selectedRow.Cells["FrameType2"].Value.ToString();

            // SID + Data 값을 생성
            string fullDataString = sid + " " + dataString;

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
            string[] dataHexArr = fullDataString.Split(' ');
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

            string type = "Flow Control"; // FC의 Type은 고정

            // 전송
            TPCANStatus stsResult = PCANBasic.Write(handleToUse, ref canMsg);
            if (stsResult == TPCANStatus.PCAN_ERROR_OK)
            {
                // 전송 성공 -> TX 로그
                DateTime now = DateTime.Now;
                string dataHex = string.Join(" ", msgData.Take(8).Select(b => b.ToString("X2")));

                LogEntry txEntry = new LogEntry(
                    "TX",
                    now,
                    type,
                    canIdHex,
                    canMsg.LEN,
                    dataHex,
                    "Flow Control TX : " + data_description
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
                MessageBox.Show("FC Transmit failed: " + stsResult.ToString());
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

            string sid = dataHexArray[0];
            string dataString = string.Join(" ", dataHexArray.Skip(1).Select(s => s.ToUpper()));
            //string frameType = GetLogTypeFromData(dataString);
            currentType1 = GetLogTypeFromData(dataString);
            string frameType = currentType1;

            int rowIndex = dataGridView1.Rows.Add();
            dataGridView1.Rows[rowIndex].Cells[0].Value = msgIdHex.ToUpper();
            dataGridView1.Rows[rowIndex].Cells[1].Value = sid;
            dataGridView1.Rows[rowIndex].Cells[2].Value = dataString;
            dataGridView1.Rows[rowIndex].Cells[3].Value = frameType;
            dataGridView1.Rows[rowIndex].Cells[4].Value = data_description;

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
        //   FlowControl (FC_Set) / FlowControlDelete (GridView 행 편집)
        //======================================================================
        private void FC_Set_Click(object sender, EventArgs e)
        {
            // FC ID 입력 확인
            string fcIdHex = FC_ID_Edit.Text.Trim();
            if (string.IsNullOrEmpty(fcIdHex))
            {
                MessageBox.Show("Please enter Flow Control ID.");
                return;
            }

            // 데이터 바이트 배열 생성
            string[] fcDataArray = new string[8];
            for (int i = 0; i < 8; i++)
            {
                string boxName = "tb_fc" + i.ToString();
                Control[] boxes = this.Controls.Find(boxName, true);
                if (boxes.Length > 0 && boxes[0] is TextBox)
                {
                    TextBox t = (TextBox)boxes[0];
                    string hexVal = t.Text.Trim();
                    if (string.IsNullOrEmpty(hexVal))
                        hexVal = "00"; // 빈 값은 00으로 설정
                    fcDataArray[i] = hexVal;
                }
                else
                {
                    fcDataArray[i] = "00"; // 기본 값
                }
            }

            // FS, BS, ST_MIN 값 설정
            string fs = fcDataArray[0]; // FS 값은 첫 번째 바이트
            string bs = tb_bs.Text.Trim(); // Block Size
            string stMin;
            if (rBtn_ms.Checked)
                stMin = ((int)udStmin_ms.Value).ToString("X2");
            else if (rBtn_μs.Checked)
                stMin = ((int)dud_STmin_us.Value).ToString("X2");
            else
                stMin = "00"; // 기본 ST_MIN 값

            // FC 데이터 문자열 생성
            string dataString = string.Join(" ", fcDataArray.Select(s => s.ToUpper()));
            string frameType = "Flow Control";
            string description = "Flow Control Created";

            // dataGridView2에 데이터 추가
            int rowIndex = dataGridView2.Rows.Add();
            dataGridView2.Rows[rowIndex].Cells[0].Value = fcIdHex.ToUpper(); // FC ID
            dataGridView2.Rows[rowIndex].Cells[1].Value = fs; // FS 값
            dataGridView2.Rows[rowIndex].Cells[2].Value = dataString; // FC 데이터
            dataGridView2.Rows[rowIndex].Cells[3].Value = bs.ToUpper(); // Block Size
            dataGridView2.Rows[rowIndex].Cells[4].Value = stMin; // ST_MIN 값
            dataGridView2.Rows[rowIndex].Cells[5].Value = frameType; // 프레임 타입
            dataGridView2.Rows[rowIndex].Cells[6].Value = description; // 설명

            MessageBox.Show("Flow Control Created / Added to Grid.");
        }

        private void FcDelete_Click(object sender, EventArgs e)
        {
            if (dataGridView2.SelectedRows.Count == 0)
            {
                MessageBox.Show("No row is selected for deletion!");
                return;
            }

            foreach (DataGridViewRow row in dataGridView2.SelectedRows)
            {
                if (!row.IsNewRow)
                    dataGridView2.Rows.Remove(row);
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
                        currentType1 = "Standard";
                        break;
                    case 0x85:
                        currentType1 = "EcuProgramming";
                        break;
                    case 0x90:
                        currentType1 = "Extended";
                        break;
                    default:
                        // 새로운 모드 코드가 아닌 경우, 기존 상태 유지
                        break;
                }
            }

            // 현재 상태 반환
            return currentType1;
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

        //======================================================================
        //   로그 디스플레이 메서드 : logEntry 받아서 ListBox에 표시
        //======================================================================
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

            // 자동 저장 호출
            LogListBox_ItemsChanged(null, null);

            // 표시 항목 수 제한 (예: 100개)
            if (LogListBox.Items.Count > 100)
            {
                LogListBox.Items.RemoveAt(0); // 가장 오래된 항목 제거
            }

            // 마지막 항목으로 스크롤
            LogListBox.TopIndex = LogListBox.Items.Count - 1;
        }

        //===============================================================================================
        //   명령어 즐겨찾기, 명령어 tb_byte에 적용, 로그 String 생성 메서드, description 별개 생성 메서드
        //===============================================================================================

        // 명령어 즐겨찾기 하드 코딩
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
                    GetDescriptionForCommand(selectedNode.Name);
                    break;

                case "StartDiagnostic":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x10, 0x81 });

                    break;

                case "StopDiagnostic":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x20 });

                    break;

                // === Read ECU Identification ID ===
                case "VehicleProject":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x91 });

                    break;

                case "EcuIdentification":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x80 });

                    break;

                case "HMC/KMC":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x86 });

                    break;

                case "VIN":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x90 });

                    break;

                case "ReadSensors":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x8B });

                    break;

                case "ManufacturerPart":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x87 });

                    break;

                // === Read DTC By Status ===
                case "ActiveFault":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x18, 0x00, 0x40, 0x00 });

                    break;

                case "HistoricFault":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x18, 0x01, 0x40, 0x00 });

                    break;

                // === Clear Diagnostic Information ===
                case "ClearAll":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x00 });

                    break;

                case "ActiveDTCS":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x01 });

                    break;

                case "HistoricDTCS":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x14, 0x40, 0x02 });

                    break;

                ////////////////////////////////////
                /// === ECU Programming Mode === ///
                ////////////////////////////////////

                case "nodeECUProgrammingMode":
                    MessageBox.Show("ECU Programming Mode 선택됨");
                    GetDescriptionForCommand(selectedNode.Name);

                    break;

                case "StartDiagnostic2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x10, 0x85 });

                    break;

                // === Read Data By Local Identifier ===
                case "ECUInputBattery":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x21, 0x01 });

                    break;

                case "LampDrive":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x21, 0x02 });

                    break;

                case "SensorStatus":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x21, 0x06 });

                    break;

                case "EcuStatus":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x21, 0xAF });

                    break;

                // === Write Data By Local Identifier ===
                case "VehicleProject&WheelSize":
                    GetDescriptionForCommand(selectedNode.Name);

                    using (WriteVehicle vehicleForm = new WriteVehicle()) // 서브 폼 열기
                    {
                        if (vehicleForm.ShowDialog() == DialogResult.OK)
                        {
                            // 서브 폼에서 입력된 메시지를 받아오기
                            byte[] message = vehicleForm.VehicleMessageBytes;

                            // 메시지 검증
                            if (message != null && message.Length == 8)
                            {
                                SendCanCommand(message); // 메시지 전송
                            }
                            else
                            {
                                MessageBox.Show("유효하지 않은 메시지입니다.");
                            }
                        }
                        else
                        {
                            MessageBox.Show("메시지 생성이 취소되었습니다.");
                        }
                    }
                    //SendCanCommand(new byte[] { 0x3B, 0x91, 0x46, 0x53, 0x31, 0x54, 0x00, 0x00 });

                    break;

                case "ECUIdentificationData":
                    GetDescriptionForCommand(selectedNode.Name);

                    using (WriteECU ecuForm = new WriteECU()) // 서브 폼 열기
                    {
                        if (ecuForm.ShowDialog() == DialogResult.OK)
                        {
                            // 서브 폼에서 생성된 메시지를 가져옴
                            byte[] message = ecuForm.ECUMessageBytes;

                            // 메시지 검증
                            if (message != null && message.Length == 18)
                            {
                                SendCanCommand(message); // 메시지 전송
                            }
                            else
                            {
                                MessageBox.Show("유효하지 않은 메시지입니다.");
                            }
                        }
                        else
                        {
                            MessageBox.Show("메시지 생성이 취소되었습니다.");
                        }
                    }
                    //SendCanCommand(new byte[] { 0x3B, 0x80, 0x54, 0x50, 0x4D, 0x53, 0x48, 0x49, 0x47, 0x48, 0x5F, 0x4C, 0x49, 0x4E, 0x45 });

                    break;

                case "HMC/KMCData":
                    GetDescriptionForCommand(selectedNode.Name);
                    using (WriteHMC hmcForm = new WriteHMC()) // 서브 폼 열기
                    {
                        if (hmcForm.ShowDialog() == DialogResult.OK)
                        {
                            // 서브 폼에서 생성된 메시지를 가져옴
                            byte[] message = hmcForm.HMCMessageBytes;

                            // 메시지 검증
                            if (message != null && message.Length == 5)
                            {
                                SendCanCommand(message); // 메시지 전송
                            }
                            else
                            {
                                MessageBox.Show("유효하지 않은 메시지입니다.");
                            }
                        }
                        else
                        {
                            MessageBox.Show("메시지 생성이 취소되었습니다.");
                        }
                    }
                    //SendCanCommand(new byte[] { 0x3B, 0x86, 0x04, 0x02, 0x02 });

                    break;

                case "VINData":
                    GetDescriptionForCommand(selectedNode.Name);
                    using (WriteVIN vinForm = new WriteVIN()) // 서브 폼 열기
                    {
                        if (vinForm.ShowDialog() == DialogResult.OK)
                        {
                            // VIN 메시지 가져오기
                            byte[] vinMessage = vinForm.VINMessageBytes;

                            // SendCanCommand 호출
                            if (vinMessage != null && vinMessage.Length == 19)
                            {
                                SendCanCommand(vinMessage);
                            }
                            else
                            {
                                MessageBox.Show("VIN 메시지가 유효하지 않습니다.");
                            }
                        }
                        else
                        {
                            MessageBox.Show("VIN 메시지 입력이 취소되었습니다.");
                        }
                    }

                    //SendCanCommand(new byte[] { 0x3B, 0x90, 0x56, 0x49, 0x4E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

                    break;

                case "SensorIDType":
                    GetDescriptionForCommand(selectedNode.Name);

                    using (WriteSensor sensorForm = new WriteSensor()) // 서브 폼 열기
                    {
                        if (sensorForm.ShowDialog() == DialogResult.OK)
                        {
                            // Sensor 메시지 가져오기
                            byte[] sensorMessage = sensorForm.SensorMessageBytes;

                            // SendCanCommand 호출
                            if (sensorMessage != null && sensorMessage.Length == 18)
                            {
                                SendCanCommand(sensorMessage);
                            }
                            else
                            {
                                MessageBox.Show("Sensor 메시지가 유효하지 않습니다. 데이터를 확인해주세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            MessageBox.Show("Sensor 메시지 입력이 취소되었습니다.", "취소", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }

                    //SendCanCommand(new byte[] { 0x3B, 0x8B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

                    break;

                case "ManufacturePartInfo":
                    GetDescriptionForCommand(selectedNode.Name);

                    using (WriteManufacture manufactureForm = new WriteManufacture()) // 서브 폼 열기
                    {
                        if (manufactureForm.ShowDialog() == DialogResult.OK)
                        {
                            // Manufacture 메시지 가져오기
                            byte[] manufactureMessage = manufactureForm.ManufactureMessageBytes;

                            // SendCanCommand 호출
                            if (manufactureMessage != null && manufactureMessage.Length == 42)
                            {
                                SendCanCommand(manufactureMessage);
                            }
                            else
                            {
                                MessageBox.Show("Manufacture 메시지가 유효하지 않습니다.");
                            }
                        }
                        else
                        {
                            MessageBox.Show("Manufacture 메시지 입력이 취소되었습니다.");
                        }
                    }
                    /*SendCanCommand(new byte[] { 0x3B, 0x87, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    , 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    , 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                    , 0x31, 0x30, 0x31, 0x30
                    , 0x00, 0x00, 0x00, 0x00});*/

                    break;

                //////////////////////////////////////
                // === Extended Diagnostic Mode === //
                //////////////////////////////////////

                case "nodeExtended":
                    MessageBox.Show("Extended Diagnostic Mode 선택됨");
                    GetDescriptionForCommand(selectedNode.Name);

                    break;

                case "StartDiagnostic3":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x10, 0x90 });

                    break;

                case "StopDiagnostic2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x20 });

                    break;

                // === Read ECU Identification ID ===
                case "VehicleProject2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x91 });

                    break;

                case "EcuIdentification2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x80 });

                    break;

                case "HMC/KMC2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x86 });

                    break;

                case "VIN2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x90 });

                    break;

                case "ReadSensors2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x8B });

                    break;

                case "ManufacturerPart2":
                    GetDescriptionForCommand(selectedNode.Name);
                    SendCanCommand(new byte[] { 0x1A, 0x87 });

                    break;

                default:
                    MessageBox.Show("알 수 없는 노드 선택됨: {0}", selectedNode.Text);
                    GetDescriptionForCommand(selectedNode.Name);

                    break;
            }
        }

        //Tx description 적용 메서드
        private string GetDescriptionForCommand(string commandKey)
        {
            switch (commandKey)
            {
                case "StartDiagnostic":
                    return data_description = "Start Diagnostic Mode";
                case "StopDiagnostic":
                    return data_description = "Stop Diagnostic Mode";
                case "VehicleProject":
                    return data_description = "Request Vehicle Project Information";
                case "EcuIdentification":
                    return data_description = "Request ECU Identification Data";
                case "HMC/KMC":
                    return data_description = "Request HMC/KMC Part Configuration";
                case "VIN":
                    return data_description = "Request Vehicle Identification Number (VIN)";
                case "ReadSensors":
                    return data_description = "Request Sensor Data";
                case "ManufacturerPart":
                    return data_description = "Request Manufacturer Part Information";
                case "ActiveFault":
                    return data_description = "Request Active Fault (Current DTC)";
                case "HistoricFault":
                    return data_description = "Request Historic Fault (Historical DTC)";
                case "ClearAll":
                    return data_description = "Clear All Historic and Active DTC Information";
                case "ActiveDTCS":
                    return data_description = "Active DTCs Changed to Historic DTCs";
                case "HistoricDTCS":
                    return data_description = "Historic DTCs Changed to Active DTCs";
                case "StartDiagnostic2":
                    return data_description = "Start ECU Programming Diagnostic Mode";
                case "ECUInputBattery":
                    return data_description = "Request ECU Input Battery Values";
                case "LampDrive":
                    return data_description = "Request Lamp Drive Status";
                case "SensorStatus":
                    return data_description = "Request Sensor Status Information";
                case "EcuStatus":
                    return data_description = "Request ECU Status Information";
                case "VehicleProject&WheelSize":
                    return data_description = "Set Vehicle Project Name and Wheel Size";
                case "ECUIdentificationData":
                    return data_description = "Set ECU Identification Data Table";
                case "HMC/KMCData":
                    return data_description = "Set HMC/KMC Part Configuration";
                case "VINData":
                    return data_description = "Set Vehicle Identification Number (VIN)";
                case "SensorIDType":
                    return data_description = "Set Sensor ID Type 1 Learn";
                case "ManufacturePartInfo":
                    return data_description = "Set Manufacturer Part Information Block";
                case "StartDiagnostic3":
                    return data_description = "Start Extended Diagnostic Mode";
                case "StopDiagnostic2":
                    return data_description = "Stop Extended Diagnostic Mode";
                case "VehicleProject2":
                    return data_description = "Request Vehicle Project Information (Extended)";
                case "EcuIdentification2":
                    return data_description = "Request ECU Identification Data (Extended)";
                case "HMC/KMC2":
                    return data_description = "Request HMC/KMC Part Configuration (Extended)";
                case "VIN2":
                    return data_description = "Request Vehicle Identification Number (VIN) (Extended)";
                case "ReadSensors2":
                    return data_description = "Request Sensor Data (Extended)";
                case "ManufacturerPart2":
                    return data_description = "Request Manufacturer Part Information (Extended)";
                default:
                    return data_description = "Unknown Command";
            }
        }

        //===============================================================================================
        //   명령어 전송 관련 메서드
        //===============================================================================================

        // CAN 명령 데이터를 tb_byte0 ~ tb_byte7에 적용하는 함수
        private void SendCanCommand(byte[] command)
        {
            string description = data_description;

            // 명령 데이터가 7바이트 초과인 경우 SF로 하지않고 FF, CF 방식으로 진행
            if (command.Length > 7)
            {
                //SF방식은 tb_byte0~ tb_byte7에 적용해서 transmit을 눌러진행했지만, 여기서는 바로 설정해서 FF, CF를 보내줄 수 있게 설정.

                // FF 방식으로 메시지 전송
                SendFirstFrame(command, description);

                // CF 방식으로 메시지 전송
                SendConsecutiveFrames(command, description);
            }
            else
            {
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
        }

        private void SendFirstFrame(byte[] command, string description)
        {
            // FF 데이터 구성
            int totalLength = command.Length;
            byte[] firstFrame = new byte[8];
            string type = "FirstFrame";

            // FF Header: N_PCI (4-bit 타입 + 12-bit 전체 길이)
            firstFrame[0] = (byte)((0x10) | ((totalLength >> 8) & 0x0F));
            firstFrame[1] = (byte)(totalLength & 0xFF);

            // FF Payload: 데이터의 첫 부분
            Array.Copy(command, 0, firstFrame, 2, 6);

            // CAN 메시지로 전송
            SendCanMessage(0x7D6, type, firstFrame, "First Frame : " + description);
        }

        private void SendConsecutiveFrames(byte[] command, string description)
        {
            // CF 데이터 구성
            int totalLength = command.Length;
            int sentBytes = 6; // FF에서 이미 6바이트 전송
            int sequenceNumber = 1;
            string type = "SendConsecutiveFrame";

            while (sentBytes < totalLength)
            {
                byte[] consecutiveFrame = new byte[8];
                consecutiveFrame[0] = (byte)((0x20) | (sequenceNumber & 0x0F)); // CF Header: N_PCI (4-bit 타입 + 4-bit 시퀀스 번호)

                int remainingBytes = totalLength - sentBytes;
                int bytesToSend = Math.Min(remainingBytes, 7);

                Array.Copy(command, sentBytes, consecutiveFrame, 1, bytesToSend);
                sentBytes += bytesToSend;

                // CAN 메시지로 전송
                SendCanMessage(0x7D6, type, consecutiveFrame, String.Format("Consecutive Frame {0} : {1}", sequenceNumber, description));

                // 시퀀스 번호 증가
                sequenceNumber = (sequenceNumber + 1) % 16;

                // STmin 대기 시간 (5ms로 가정)
                Thread.Sleep(5);
            }
        }

        private void SendCanMessage(uint canId, string type, byte[] message, string description)
        {
            // CAN 메시지 설정
            TPCANMsg canMessage = new TPCANMsg();
            canMessage.ID = canId;
            canMessage.LEN = (byte)message.Length;
            canMessage.DATA = message;

            // CAN 메시지 전송
            TPCANStatus status = PCANBasic.Write(m_PcanHandleCH1, ref canMessage);

            if (status == TPCANStatus.PCAN_ERROR_OK)
            {
                // 성공적으로 전송된 경우 로그 추가
                DateTime now = DateTime.Now;
                string dataHex = String.Join(" ", message.Select(b => b.ToString("X2")).ToArray());

                string logEntry = String.Format("{0:yyyy-MM-dd HH:mm:ss.fff} | TX | {1} | ID={2:X3} | Len={3} | Data={4} | {5}",
                    now, type, canId, message.Length, dataHex, description);

                // LogListBox에 추가
                UpdateDisplay(logEntry);

                // TxLog.txt에 기록
                SaveTxLog(logEntry);
            }
            else
            {
                MessageBox.Show(String.Format("CAN 메시지 전송 실패: {0}", status.ToString()));
            }
        }

        private void SaveTxLog(string logEntry)
        {
            string txPath = Path.Combine(folderPath, txLogFile);

            // 로그를 TxLog.txt에 저장
            using (StreamWriter sw = new StreamWriter(txPath, true))
            {
                sw.WriteLine(logEntry);
            }
        }

        //===============================================================================================
        //   TX(Read)일떄 RX 데이터 파싱 관련 메서드
        //===============================================================================================

        // Manufacturer Part Information 처리
        private string ParseManufacturerPartInfo(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 40; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            // Manufacturer Part Numbers: ASCII 값에서 문자열로 변환
            string partNum1 = Encoding.ASCII.GetString(data.GetRange(2, 12).ToArray()).TrimEnd('\0'); // Manufacturer Part Number1
            string partNum2 = Encoding.ASCII.GetString(data.GetRange(14, 12).ToArray()).TrimEnd('\0'); // Manufacturer Part Number2

            // 날짜(Date) 처리: ASCII 값 -> 문자열로 변환
            string year = Encoding.ASCII.GetString(data.GetRange(26, 4).ToArray()).TrimEnd('\0'); // Year
            string month = Encoding.ASCII.GetString(data.GetRange(30, 2).ToArray()).TrimEnd('\0'); // Month
            string day = Encoding.ASCII.GetString(data.GetRange(32, 2).ToArray()).TrimEnd('\0'); // Day
            string date = string.Format("{0}/{1}/{2}", year, month, day);

            // H/W와 S/W Version: ASCII 값 -> 문자열로 변환
            string hw = Encoding.ASCII.GetString(data.GetRange(34, 2).ToArray()).TrimEnd('\0'); // H/W Version
            string sw = Encoding.ASCII.GetString(data.GetRange(36, 2).ToArray()).TrimEnd('\0'); // S/W Version

            // Ignition Count 처리: 헥사 값을 각각 십진수로 변환
            int ignitionCount1 = data[38]; // 상위 바이트
            int ignitionCount2 = data[39]; // 하위 바이트
            string ignitionCount = (ignitionCount1 * 256 + ignitionCount2).ToString(); // 상위 바이트와 하위 바이트를 결합한 값

            // 최종 파싱된 정보 생성
            parsedInfo = string.Format(
                "Manufacturer Part Number 1: {0}, Manufacturer Part Number 2: {1}, Date: {2}, H/W Version: {3}, S/W Version: {4}, Ignition Count Since Factory: {5}",
                partNum1, partNum2, date, hw, sw, ignitionCount
            );

            return parsedInfo;
        }

        // Sensor ID 처리
        private string ParseSensorID(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 18; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            // Sensor ID 1~4를 16진수 → 10진수로 변환
            int sensorID1 = Convert.ToInt32(BitConverter.ToString(data.GetRange(2, 4).ToArray()).Replace("-", ""), 16);
            int sensorID2 = Convert.ToInt32(BitConverter.ToString(data.GetRange(6, 4).ToArray()).Replace("-", ""), 16);
            int sensorID3 = Convert.ToInt32(BitConverter.ToString(data.GetRange(10, 4).ToArray()).Replace("-", ""), 16);
            int sensorID4 = Convert.ToInt32(BitConverter.ToString(data.GetRange(14, 4).ToArray()).Replace("-", ""), 16);

            parsedInfo = string.Format("Sensor ID 1: {0}, Sensor ID 2: {1}, Sensor ID 3: {2}, Sensor ID 4: {3}",
                sensorID1, sensorID2, sensorID3, sensorID4);

            return parsedInfo;
        }

        // VIN 처리
        private string ParseVIN(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 19; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            string vin = Encoding.ASCII.GetString(data.GetRange(2, 17).ToArray()).TrimEnd('\0');
            parsedInfo = string.Format("VIN: {0}", vin);

            return parsedInfo;
        }

        // HMC/KMC 처리
        private string ParseHMCKMC(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 5; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            string numSensor = data[3] == 0x04 ? "4" : data[3] == 0x05 ? "5" : "Unknown";
            string levelSet = data[4] == 0x01 ? "Low Line" : data[4] == 0x02 ? "High Line" : "Unknown";
            string rfConfig = data[5] == 0x01 ? "315MHz" : data[5] == 0x02 ? "433.92MHz" : "Unknown";

            parsedInfo = string.Format("Number of Sensors: {0}, System Level Setting: {1}, RF Configuration: {2}",
                numSensor, levelSet, rfConfig);

            return parsedInfo;
        }

        // ECU Identification 처리
        private string ParseECUIdentification(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 16; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            string hmckmc = Encoding.ASCII.GetString(data.GetRange(2, 4).ToArray()).TrimEnd('\0');
            string blank = Encoding.ASCII.GetString(data.GetRange(6, 1).ToArray()).TrimEnd('\0');
            string hkSysLev = Encoding.ASCII.GetString(data.GetRange(7, 9).ToArray()).TrimEnd('\0');

            parsedInfo = string.Format("HMC/KMC System Name: {0}, Blank: {1}, HMC/KMC System Level: {2}",
                hmckmc, blank, hkSysLev);

            return parsedInfo;
        }

        // Vehicle Project Name 처리
        private string ParseVehicleProjectName(List<byte> data)
        {
            string parsedInfo;
            int requiredLength = 8; // 최소 데이터 길이

            // 패딩 처리
            data = EnsureDataLength(data, requiredLength);

            string projectName = Encoding.ASCII.GetString(data.GetRange(2, 6).ToArray()).TrimEnd('\0');
            parsedInfo = string.Format("Project Name: {0}", projectName);

            return parsedInfo;
        }

        // 패딩 처리 메서드 (기존 데이터 유지, 부족한 부분만 0으로 채움)
        private List<byte> EnsureDataLength(List<byte> data, int requiredLength)
        {
            if (data.Count < requiredLength)
            {
                List<byte> paddedData = new List<byte>(data); // 기존 데이터 복사
                paddedData.AddRange(Enumerable.Repeat((byte)0, requiredLength - data.Count)); // 부족한 자리 0으로 채움
                return paddedData;
            }
            return data;
        }

        // Rx 데이터 파싱
        private List<byte> ParseRxData(TPCANMsg message)
        {
            if (message.DATA[0] >> 4 == 0x1) // First Frame (FF)
            {
                // First Frame: 기존 데이터 초기화 및 데이터 추가
                receivedData2.Clear();
                expectedDataLength = ((message.DATA[0] & 0x0F) << 8) | message.DATA[1];
                receivedData2.AddRange(message.DATA.Skip(2).Take(message.LEN - 2));
            }
            else if (message.DATA[0] >> 4 == 0x2) // Consecutive Frame (CF)
            {
                // Consecutive Frame: 데이터 이어 붙이기
                receivedData2.AddRange(message.DATA.Skip(1).Take(message.LEN - 1));
            }

            return receivedData2;
        }

        //Rx로그의 Description 내용으로 판단
        private void ProcessRxData(TPCANMsg message, string description)
        {
            ParseRxData(message); // MultiFrame 데이터 병합

            string parsedData = string.Empty;

            // MultiFrame 처리
            if (message.DATA[0] >> 4 == 0x10 || (message.DATA[0] >= 0x21 && message.DATA[0] < 0x30))
            {
                // Description에 따라 데이터 파싱
                if (description.Contains("Request Manufacturer Part"))
                {
                    parsedData = ParseManufacturerPartInfo(receivedData2);
                }
                else if (description.Contains("Request Sensor"))
                {
                    parsedData = ParseSensorID(receivedData2);
                }
                else if (description.Contains("Request Vehicle Identification"))
                {
                    parsedData = ParseVIN(receivedData2);
                }
                else if (description.Contains("Request HMC/KMC"))
                {
                    parsedData = ParseHMCKMC(receivedData2);
                }
                else if (description.Contains("Request ECU Identification"))
                {
                    parsedData = ParseECUIdentification(receivedData2);
                }
                else if (description.Contains("Request Vehicle Project"))
                {
                    parsedData = ParseVehicleProjectName(receivedData2);
                }

                // 데이터 길이가 예상 길이에 도달한 경우 추가 정보 출력
                if (receivedData2.Count >= expectedDataLength)
                {
                    if (!string.IsNullOrEmpty(parsedData))
                    {
                        LogAdditionalDescription(parsedData);
                    }

                    receivedData2.Clear();
                    expectedDataLength = 0;
                }
            }
            // SingleFrame 처리
            else if (message.DATA[0] >> 4 == 0x00) // SingleFrame (0x00 indicates SF in PCI byte)
            {
                // Description에 따라 데이터 파싱
                if (description.Contains("Request HMC/KMC"))
                {
                    // SingleFrame 데이터 그대로 파싱
                    parsedData = ParseHMCKMC(new List<byte>(message.DATA.Take(message.LEN)));

                    if (!string.IsNullOrEmpty(parsedData))
                    {
                        LogAdditionalDescription(parsedData);
                    }
                    else
                    {

                    }
                }      
            }
            else if (message.DATA[1] == 0x7F)
            {
                // 특정 에러 메시지 처리 (필요하면 추가)
            }
        }

        //===============================================================================================
        //   LogList 업데이트 관련 메서드
        //===============================================================================================

        private void UpdateDisplay(string logEntry)
        {
            if (LogListBox.InvokeRequired)
            {
                LogListBox.Invoke(new System.Action(() =>
                {
                    LogListBox.Items.Add(logEntry);

                    // 자동 저장 호출
                    LogListBox_ItemsChanged(null, null);

                    // 표시 항목 수 제한 (예: 100개)
                    if (LogListBox.Items.Count > 100)
                    {
                        LogListBox.Items.RemoveAt(0);
                    }

                    // 마지막 항목으로 스크롤
                    LogListBox.TopIndex = LogListBox.Items.Count - 1;
                }));
            }
            else
            {
                LogListBox.Items.Add(logEntry);

                // 자동 저장 호출
                LogListBox_ItemsChanged(null, null);

                // 표시 항목 수 제한 (예: 100개)
                if (LogListBox.Items.Count > 100)
                {
                    LogListBox.Items.RemoveAt(0);
                }

                // 마지막 항목으로 스크롤
                LogListBox.TopIndex = LogListBox.Items.Count - 1;
            }
        }

        //Read Tx일때 Rx의 ReadDescription 로그에 추가
        private void LogAdditionalDescription(string additionalDescription)
        {
            if (LogListBox.InvokeRequired)
            {
                LogListBox.Invoke(new System.Action(() =>
                {
                    LogListBox.Items.Add("Read Info [ " + additionalDescription + " ]");

                    // 자동 저장 호출
                    LogListBox_ItemsChanged(null, null);

                    // 표시 항목 수 제한 (예: 100개)
                    if (LogListBox.Items.Count > 100)
                    {
                        LogListBox.Items.RemoveAt(0);
                    }

                    // 마지막 항목으로 스크롤
                    LogListBox.TopIndex = LogListBox.Items.Count - 1;
                }));
            }
            else
            {
                LogListBox.Items.Add("Read Info: [ " + additionalDescription + " ]");

                // 자동 저장 호출
                LogListBox_ItemsChanged(null, null);

                // 표시 항목 수 제한 (예: 100개)
                if (LogListBox.Items.Count > 100)
                {
                    LogListBox.Items.RemoveAt(0);
                }

                // 마지막 항목으로 스크롤
                LogListBox.TopIndex = LogListBox.Items.Count - 1;
            }
        }

        //LogListBox에 Display되어있는 값들 지우기
        private void LogResetButton_Click(object sender, EventArgs e)
        {
            // LogListBox 항목 모두 제거
            LogListBox.Items.Clear();

            // 메시지 박스로 사용자에게 초기화 확인
            MessageBox.Show("Log has been reset.", "Log Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LogListBox_ItemsChanged(object sender, EventArgs e)
        {
            try
            {
                // 저장 파일 경로
                string logListDisplayPath = Path.Combine(folderPath, "LogListDisplay.txt");

                // LogListBox의 모든 항목을 파일에 저장
                using (StreamWriter writer = new StreamWriter(logListDisplayPath, false))
                {
                    foreach (var item in LogListBox.Items)
                    {
                        writer.WriteLine(item.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // 예외 발생 시 로그에 남길 수 있음 (선택 사항)
                Console.WriteLine("LogListBox 자동 저장 중 오류 발생: " + ex.Message);
            }
        }

        //===============================================================================================
        //   로그를 담을 클래스
        //===============================================================================================

        public class LogEntry
        {
            public string Direction;   // "TX" or "RX"

            public DateTime Timestamp;
            public string CanIdHex;    // 예: "7D6"
            public int DataLength;     // 0~8
            public string DataHex;     // 예: "00 11 22 33 ..."
            public string Description; // 추가 설명
            public string Type;

            public LogEntry(string dir, DateTime ts, string type, string idHex, int len, string data, string desc = "")
            {
                Direction = dir;
                Timestamp = ts;
                Type = type;
                CanIdHex = idHex;
                DataLength = len;
                DataHex = data;
                Description = desc;
            }

            public override string ToString()
            {
                // 예) 2023-10-12 14:08:32.123 | TX | Standard | ID=7B7 | Len=8 | Data=00 11 22 33 44 55 66 77
                return string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2} | ID={3} | Len={4} | Data={5} | {6}",
                    Timestamp, Direction, Type, CanIdHex, DataLength, DataHex, Description);
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
                            Type = "Standard";
                            break;
                        case 0x85:
                            Type = "EcuProgramming";
                            break;
                        case 0x90:
                            Type = "Extended";
                            break;
                        default:
                            break;
                    }
                }
                return "Unknown";
            }
        }
    }
}
