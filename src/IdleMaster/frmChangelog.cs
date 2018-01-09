using System;
using System.Windows.Forms;
using IdleMaster.Properties;

namespace IdleMaster
{
    public partial class FrmChangelog : Form
    {
        public FrmChangelog()
        {
            InitializeComponent();
        }

        private void FrmChangelog_Load(object sender, EventArgs e)
        {
            // Localize Form
            Text = localization.strings.release_notes_title;

            rtbChangelog.Rtf = Resources.Changelog;
        }
    }
}