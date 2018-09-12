using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ColorChord.NET
{
    public partial class MainForm : Form
    {
        private bool IsClosing = false;

        public MainForm()
        {
            InitializeComponent();
        }

        private void TrayIcon_Click(object sender, EventArgs e)
        {
            Program.OutputEnabled = !Program.OutputEnabled;
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
