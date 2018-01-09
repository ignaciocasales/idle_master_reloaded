using System;
using System.Deployment.Application;
using System.Reflection;
using System.Windows.Forms;

namespace IdleMaster
{
    public partial class FrmAbout : Form
    {
        public FrmAbout()
        {
            InitializeComponent();
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void FrmAbout_Load(object sender, EventArgs e)
        {
            // Localize the form
            btnOK.Text = localization.strings.ok;

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                var version = ApplicationDeployment.CurrentDeployment.CurrentVersion.ToString();
                lblVersion.Text = $"Idle Master v{version}";
            }
            else
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                lblVersion.Text = $"Idle Master v{version}";
            }
        }
    }
}