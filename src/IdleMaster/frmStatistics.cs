using System;
using System.Windows.Forms;

namespace IdleMaster
{
    public partial class FrmStatistics : Form
    {
        private readonly Statistics _statistics;

        public FrmStatistics(Statistics statistics)
        {
            InitializeComponent();
            this._statistics = statistics;
        }

        private void FrmStatistics_Load(object sender, EventArgs e)
        {
            // Localize Form
            Text = localization.strings.statistics.Replace("&", "");
            btnOK.Text = localization.strings.accept;
            lblSessionHeader.Text = localization.strings.this_session + ":";
            lblTotalHeader.Text = localization.strings.total + ":";

            var sessionMinutesIdled = TimeSpan.FromMinutes(_statistics.getSessionMinutesIdled());
            var totalMinutesIdled = TimeSpan.FromMinutes(Properties.Settings.Default.totalMinutesIdled);

            var sessionHoursIdled = sessionMinutesIdled.Days * 24 + sessionMinutesIdled.Hours;
            var totalHoursIdled = totalMinutesIdled.Days * 24 + totalMinutesIdled.Hours;

            //Session
            if (sessionHoursIdled > 0)
            {
                lblSessionTime.Text =
                    $"{sessionHoursIdled} hour{(sessionHoursIdled == 1 ? "" : "s")}, {sessionMinutesIdled.Minutes} minute{(sessionMinutesIdled.Minutes == 1 ? "" : "s")} idled";
            }
            else
            {
                lblSessionTime.Text =
                    $"{sessionMinutesIdled.Minutes} minute{(sessionMinutesIdled.Minutes == 1 ? "" : "s")} idled";
            }
            lblSessionCards.Text = _statistics.getSessionCardIdled().ToString() + " cards idled";

            //Total
            if (totalHoursIdled > 0)
            {
                lblTotalTime.Text =
                    $"{totalHoursIdled} hour{(totalHoursIdled == 1 ? "" : "s")}, {totalMinutesIdled.Minutes} minute{(totalMinutesIdled.Minutes == 1 ? "" : "s")} idled";
            }
            else
            {
                lblTotalTime.Text =
                    $"{totalMinutesIdled.Minutes} minute{(totalMinutesIdled.Minutes == 1 ? "" : "s")} idled";
            }
            lblTotalCards.Text = Properties.Settings.Default.totalCardIdled.ToString() + " cards idled";
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}