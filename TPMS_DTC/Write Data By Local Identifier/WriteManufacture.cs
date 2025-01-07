using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TPMS_DTC
{
    public partial class WriteManufacture : Form
    {
        public byte[] ManufactureMessageBytes { get; private set; }

        public WriteManufacture()
        {
            InitializeComponent();
        }

        private void ManufacturerMessage_Click(object sender, EventArgs e)
        {
            // 초기 메시지 배열
            byte[] message = new byte[42];
            message[0] = 0x3B; // wm_byte1 고정값
            message[1] = 0x87; // wm_byte2 고정값

            // wm_byte3 ~ wm_byte38: ASCII 입력 처리
            for (int i = 2; i < 38; i++)
            {
                string textBoxName = "wm_byte" + (i + 1).ToString();
                TextBox textBox = this.Controls.Find(textBoxName, true).FirstOrDefault() as TextBox;
                if (textBox != null)
                {
                    string input = textBox.Text;
                    if (!string.IsNullOrEmpty(input) && input.Length == 1)
                    {
                        message[i] = (byte)input[0]; // ASCII -> byte
                    }
                    else
                    {
                        MessageBox.Show("Invalid input in " + textBoxName + ". Please enter a single ASCII character.");
                        return;
                    }
                }
            }

            // wm_byte39 ~ wm_byte42: Hexadecimal 입력 처리
            for (int i = 38; i < 42; i++)
            {
                string textBoxName = "wm_byte" + (i + 1).ToString();
                TextBox textBox = this.Controls.Find(textBoxName, true).FirstOrDefault() as TextBox;
                if (textBox != null)
                {
                    int value;
                    if (int.TryParse(textBox.Text, System.Globalization.NumberStyles.HexNumber, null, out value))
                    {
                        message[i] = (byte)value; // Hex -> byte
                    }
                    else
                    {
                        MessageBox.Show("Invalid input in " + textBoxName + ". Please enter a valid hexadecimal value.");
                        return;
                    }
                }
            }

            // 메시지 저장 및 닫기
            ManufactureMessageBytes = message;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
