using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ColorChord.NET
{
    public partial class MainForm : Form
    {
        private bool IsClosing = false;
        private UdpClient UDPListener;

        public MainForm()
        {
            InitializeComponent();
            this.UDPListener = new UdpClient(new IPEndPoint(IPAddress.Any, 10486));
            this.UDPListener.BeginReceive(HandleUDPData, this.UDPListener);
        }

        private void HandleUDPData(IAsyncResult Result)
        {
            UdpClient Listener;
            IPEndPoint ReceivedEndpoint;
            byte[] Data;
            try
            {
                Listener = (UdpClient)Result.AsyncState;
                ReceivedEndpoint = new IPEndPoint(IPAddress.Any, 0);
                Data = Listener.EndReceive(Result, ref ReceivedEndpoint);
                if (Data.Length == 1)
                {
                    Program.OutputEnabled = Data[0] == 1;
                    Toggle();
                }
            }
            catch { }
            this.UDPListener.BeginReceive(HandleUDPData, this.UDPListener);
        }

        private void TrayIcon_Click(object sender, EventArgs e)
        {
            Program.OutputEnabled = !Program.OutputEnabled;
            Toggle();
        }

        private void Toggle()
        {
            if (Program.OutputEnabled)
            {
                this.Text = "ColorChord.NET (On)";
            }
            else
            {
                this.Text = "ColorChord.NET (Off)";
                try { LinearOutput.SendBlack(); }
                catch { } // Meh, no big deal.
            }
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if(e.CloseReason == CloseReason.UserClosing && !IsClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void TrayIconMenuConfigure_Click(object sender, EventArgs e) => TrayIcon_DoubleClick(sender, null);
        private void TrayIconMenuToggle_Click(object sender, EventArgs e) => TrayIcon_Click(sender, null);

        private void ExitButton_Click(object sender, EventArgs e)
        {
            IsClosing = true;
            Close();
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {

        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            BeginInvoke(new MethodInvoker(delegate { Hide(); }));
        }
    }
}
