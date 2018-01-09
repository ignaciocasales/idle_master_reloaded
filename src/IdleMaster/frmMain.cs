using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using IdleMaster.Properties;
using Newtonsoft.Json;
using Steamworks;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;
using System.Globalization;
using System.Security.Principal;
using static System.String;

namespace IdleMaster
{
    public partial class FrmMain : Form
    {
        private readonly Statistics _statistics = new Statistics();
        public List<Badge> AllBadges { get; set; }

        public IEnumerable<Badge> CanIdleBadges
        {
            get { return AllBadges.Where(b => b.RemainingCard != 0); }
        }

        public bool IsCookieReady;
        public bool IsSteamReady;
        public int TimeLeft = 900;
        public int RetryCount = 0;
        public int ReloadCount = 0;

        public int CardsRemaining
        {
            get { return CanIdleBadges.Sum(b => b.RemainingCard); }
        }

        public int GamesRemaining => CanIdleBadges.Count();

        public Badge CurrentBadge;

        internal void UpdateStateInfo()
        {
            // Update totals
            if (ReloadCount != 0)
            {
                return;
            }
            lblIdle.Text =
                $@"{GamesRemaining} {localization.strings.games_left_to_idle}, {CanIdleBadges.Count(b => b.InIdle)} {
                        localization.strings.idle_now
                    }.";
            lblDrops.Text = $@"{CardsRemaining} {localization.strings.card_drops_remaining}";
            lblIdle.Visible = GamesRemaining != 0;
            lblDrops.Visible = CardsRemaining != 0;
        }

        private void CopyResource(string resourceName, string file)
        {
            using (var resource = GetType().Assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                {
                    return;
                }
                using (Stream output = File.OpenWrite(file))
                {
                    resource.CopyTo(output);
                }
            }
        }

        public void SortBadges(string method)
        {
            lblDrops.Text = localization.strings.sorting_results;
            switch (method)
            {
                case "mostcards":
                    AllBadges = AllBadges.OrderByDescending(b => b.RemainingCard).ToList();
                    break;
                case "leastcards":
                    AllBadges = AllBadges.OrderBy(b => b.RemainingCard).ToList();
                    break;
                case "mostvalue":
                    try
                    {
                        var query =
                            $"http://api.enhancedsteam.com/market_data/average_card_prices/im.php?appids={Join(",", AllBadges.Select(b => b.AppId))}";
                        var json = new WebClient() {Encoding = Encoding.UTF8}.DownloadString(query);
                        var convertedJson = JsonConvert.DeserializeObject<EnhancedSteamHelper>(json);
                        foreach (var price in convertedJson.AverageValues)
                        {
                            var badge = AllBadges.SingleOrDefault(b => b.AppId == price.AppId);
                            if (badge != null)
                            {
                                badge.AveragePrice = price.AveragePrice;
                            }
                        }
                        AllBadges = AllBadges.OrderByDescending(b => b.AveragePrice).ToList();
                    }
                    catch
                    {
                    }
                    break;
                default:
                    return;
            }
        }

        public void UpdateIdleProcesses()
        {
            foreach (var badge in CanIdleBadges.Where(b => !Equals(b, CurrentBadge)))
            {
                if (badge.HoursPlayed >= 2 && badge.InIdle)
                {
                    badge.StopIdle();
                }

                if (badge.HoursPlayed < 2 && CanIdleBadges.Count(b => b.InIdle) < 30)
                {
                    badge.Idle();
                }
            }

            RefreshGamesStateListView();

            if (!CanIdleBadges.Any(b => b.InIdle))
            {
                NextIdle();
            }

            UpdateStateInfo();
        }

        private void NextIdle()
        {
            // Stop idling the current game
            StopIdle();

            // Check if user is authenticated and if any badge left to idle
            // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
            if (IsNullOrWhiteSpace(Settings.Default.sessionid) || !IsSteamReady)
            {
                ResetClientStatus();
            }
            else
            {
                if (CanIdleBadges.Any())
                {
                    // Give the user notification that the next game will start soon
                    lblCurrentStatus.Text = localization.strings.loading_next;

                    // Make a short but random amount of time pass
                    var rand = new Random();
                    var wait = rand.Next(3, 9);
                    wait = wait * 1000;

                    tmrStartNext.Interval = wait;
                    tmrStartNext.Enabled = true;
                }
                else
                {
                    IdleComplete();
                }
            }
        }

        private void StartIdle()
        {
            // Kill all existing processes before starting any new ones
            // This prevents rogue processes from interfering with idling time and slowing card drops
            try
            {
                var username = WindowsIdentity.GetCurrent().Name;
                foreach (var process in Process.GetProcessesByName("steam-idle"))
                {
                    var searcher =
                        new ManagementObjectSearcher("Select * From Win32_Process Where ProcessID = " + process.Id);
                    var processList = searcher.Get();

                    foreach (ManagementObject obj in processList)
                    {
                        var argList = new string[] {Empty, Empty};
                        var returnVal = Convert.ToInt32(obj.InvokeMethod("GetOwner", argList));
                        if (returnVal == 0)
                        {
                            if (argList[1] + "\\" + argList[0] == username)
                            {
                                process.Kill();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            // Check if user is authenticated and if any badge left to idle
            // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
            if (IsNullOrWhiteSpace(Settings.Default.sessionid) || !IsSteamReady)
            {
                ResetClientStatus();
            }
            else
            {
                if (ReloadCount != 0)
                {
                    return;
                }
                if (CanIdleBadges.Any())
                {
                    _statistics.setRemainingCards((uint) CardsRemaining);
                    tmrStatistics.Enabled = true;
                    tmrStatistics.Start();
                    if (Settings.Default.OnlyOneGameIdle)
                    {
                        StartSoloIdle(CanIdleBadges.First());
                    }
                    else
                    {
                        if (Settings.Default.OneThenMany)
                        {
                            var multi = CanIdleBadges.Where(b => b.HoursPlayed >= 2);
                            var enumerable = multi as Badge[] ?? multi.ToArray();
                            if (enumerable.Any())
                            {
                                StartSoloIdle(enumerable.First());
                            }
                            else
                            {
                                StartMultipleIdle();
                            }
                        }
                        else
                        {
                            var multi = CanIdleBadges.Where(b => b.HoursPlayed < 2);
                            if (multi.Count() >= 2)
                            {
                                StartMultipleIdle();
                            }
                            else
                            {
                                StartSoloIdle(CanIdleBadges.First());
                            }
                        }
                    }
                }
                else
                {
                    IdleComplete();
                }

                UpdateStateInfo();
            }
        }

        public void StartSoloIdle(Badge badge)
        {
            // Set the currentAppID value
            CurrentBadge = badge;

            // Place user "In game" for card drops
            CurrentBadge.Idle();

            // Update game name
            lblGameName.Visible = true;
            lblGameName.Text = CurrentBadge.Name;

            GamesState.Visible = false;
            gameToolStripMenuItem.Enabled = true;

            // Update game image
            try
            {
                picApp.Load($"http://cdn.akamai.steamstatic.com/steam/apps/{CurrentBadge.StringId}/header_292x136.jpg");
                picApp.Visible = true;
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, $"frmMain -> StartIdle -> load pic, for id = {CurrentBadge.AppId}");
            }

            // Update label controls
            lblCurrentRemaining.Text = CurrentBadge.RemainingCard + " " + localization.strings.card_drops_remaining;
            lblCurrentStatus.Text = localization.strings.currently_ingame;
            lblHoursPlayed.Visible = true;
            lblHoursPlayed.Text = CurrentBadge.HoursPlayed + " " + localization.strings.hrs_on_record;

            // Set progress bar values and show the footer
            pbIdle.Maximum = CurrentBadge.RemainingCard;
            pbIdle.Value = 0;
            ssFooter.Visible = true;

            // Start the animated "working" gif
            picIdleStatus.Image = Resources.imgSpin;

            // Start the timer that will check if drops remain
            tmrCardDropCheck.Enabled = true;

            // Reset the timer
            TimeLeft = CurrentBadge.RemainingCard == 1 ? 300 : 900;

            // Set the correct buttons on the form for pause / resume
            btnResume.Visible = false;
            btnPause.Visible = true;
            btnSkip.Visible = true;
            resumeIdlingToolStripMenuItem.Enabled = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            skipGameToolStripMenuItem.Enabled = false;

            var scale = CreateGraphics().DpiY * 3.9;
            Height = Convert.ToInt32(scale);
        }

        public void StartMultipleIdle()
        {
            UpdateIdleProcesses();

            // Update label controls
            lblCurrentRemaining.Text = localization.strings.update_games_status;
            lblCurrentStatus.Text = localization.strings.currently_ingame;

            lblGameName.Visible = false;
            lblHoursPlayed.Visible = false;
            ssFooter.Visible = true;
            gameToolStripMenuItem.Enabled = false;

            // Start the animated "working" gif
            picIdleStatus.Image = Resources.imgSpin;

            // Start the timer that will check if drops remain
            tmrCardDropCheck.Enabled = true;

            // Reset the timer
            TimeLeft = 360;

            // Show game
            GamesState.Visible = true;
            picApp.Visible = false;
            RefreshGamesStateListView();

            // Set the correct buttons on the form for pause / resume
            btnResume.Visible = false;
            btnPause.Visible = false;
            btnSkip.Visible = false;
            resumeIdlingToolStripMenuItem.Enabled = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            skipGameToolStripMenuItem.Enabled = false;

            var scale = CreateGraphics().DpiY * 3.86;
            Height = Convert.ToInt32(scale);
        }

        private void RefreshGamesStateListView()
        {
            GamesState.Items.Clear();
            foreach (var badge in CanIdleBadges.Where(b => b.InIdle))
            {
                var line = new ListViewItem(badge.Name);
                line.SubItems.Add(badge.HoursPlayed.ToString());
                GamesState.Items.Add(line);
            }
        }

        public void StopIdle()
        {
            try
            {
                lblGameName.Visible = false;
                picApp.Image = null;
                picApp.Visible = false;
                GamesState.Visible = false;
                btnPause.Visible = false;
                btnSkip.Visible = false;
                lblCurrentStatus.Text = localization.strings.not_ingame;
                lblHoursPlayed.Visible = false;
                picIdleStatus.Image = null;

                // Stop the card drop check timer
                tmrCardDropCheck.Enabled = false;

                // Stop the statistics timer
                tmrStatistics.Stop();
                tmrStatistics.Enabled = false;

                // Hide the status bar
                ssFooter.Visible = false;

                // Resize the form
                var graphics = CreateGraphics();
                var scale = graphics.DpiY * 1.9583;
                Height = Convert.ToInt32(scale);

                // Kill the idling process
                foreach (var badge in AllBadges.Where(b => b.InIdle))
                {
                    badge.StopIdle();
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "frmMain -> StopIdle");
            }
        }

        public void IdleComplete()
        {
            // Deactivate the timer control and inform the user that the program is finished
            tmrCardDropCheck.Enabled = false;
            lblCurrentStatus.Text = localization.strings.idling_complete;

            lblGameName.Visible = false;
            btnPause.Visible = false;
            btnSkip.Visible = false;

            // Resize the form
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.9583;
            Height = Convert.ToInt32(scale);
        }


        public async Task LoadBadgesAsync()
        {
            // Settings.Default.myProfileURL = http://steamcommunity.com/id/USER
            var profileLink = Settings.Default.myProfileURL + "/badges";
            var pages = new List<string>() {"?p=1"};
            var document = new HtmlDocument();
            var pagesCount = 1;

            try
            {
                // Load Page 1 and check how many pages there are
                var pageUrl = Format("{0}/?p={1}", profileLink, 1);
                var response = await CookieClient.GetHttpAsync(pageUrl);
                // Response should be empty. User should be unauthorized.
                if (IsNullOrEmpty(response))
                {
                    RetryCount++;
                    if (RetryCount != 18)
                    {
                        throw new Exception("");
                    }
                    ResetClientStatus();
                    return;
                }
                document.LoadHtml(response);

                // If user is authenticated, check page count. If user is not authenticated, pages are different.
                var pageNodes = document.DocumentNode.SelectNodes("//a[@class=\"pagelink\"]");
                if (pageNodes != null)
                {
                    pages.AddRange(pageNodes.Select(p => p.Attributes["href"].Value).Distinct());
                    pages = pages.Distinct().ToList();
                }

                var lastPage = pages.Last().Replace("?p=", "");
                pagesCount = Convert.ToInt32(lastPage);

                // Get all badges from current page
                ProcessBadgesOnPage(document);

                // Load other pages
                for (var i = 2; i <= pagesCount; i++)
                {
                    lblDrops.Text =
                        $@"{localization.strings.reading_badge_page} {i}/{pagesCount} {
                                localization.strings.please_wait
                            }";

                    // Load Page 2+
                    pageUrl = $"{profileLink}/?p={i}";
                    response = await CookieClient.GetHttpAsync(pageUrl);
                    // Response should be empty. User should be unauthorised.
                    if (IsNullOrEmpty(response))
                    {
                        RetryCount++;
                        if (RetryCount != 18)
                        {
                            throw new Exception("");
                        }
                        ResetClientStatus();
                        return;
                    }
                    document.LoadHtml(response);

                    // Get all badges from current page
                    ProcessBadgesOnPage(document);
                }
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "Badge -> LoadBadgesAsync, for profile = " + Settings.Default.myProfileURL);
                // badge page didn't load
                picReadingPage.Image = null;
                picIdleStatus.Image = null;
                lblDrops.Text = localization.strings.badge_didnt_load.Replace("__num__", "10");
                lblIdle.Text = "";

                // Set the form height
                var graphics = CreateGraphics();
                var scale = graphics.DpiY * 1.625;
                Height = Convert.ToInt32(scale);
                ssFooter.Visible = false;

                ReloadCount = 1;
                tmrBadgeReload.Enabled = true;
                return;
            }

            RetryCount = 0;
            SortBadges(Settings.Default.sort);

            picReadingPage.Visible = false;
            UpdateStateInfo();

            if (CardsRemaining == 0)
            {
                IdleComplete();
            }
        }

        /// <summary>
        /// Processes all badges on page
        /// </summary>
        /// <param name="document">HTML document (1 page) from x</param>
        private void ProcessBadgesOnPage(HtmlDocument document)
        {
            foreach (var badge in document.DocumentNode.SelectNodes("//div[@class=\"badge_row is_link\"]"))
            {
                var appIdNode = badge.SelectSingleNode(".//a[@class=\"badge_row_overlay\"]").Attributes["href"].Value;
                var appId = Regex.Match(appIdNode, @"gamecards/(\d+)/").Groups[1].Value;

                if (IsNullOrWhiteSpace(appId) || Settings.Default.blacklist.Contains(appId) ||
                    appId == "368020" || appId == "335590" || appIdNode.Contains("border=1"))
                {
                    continue;
                }

                var hoursNode = badge.SelectSingleNode(".//div[@class=\"badge_title_stats_playtime\"]");
                var hours = hoursNode == null ? Empty : Regex.Match(hoursNode.InnerText, @"[0-9\.,]+").Value;

                var nameNode = badge.SelectSingleNode(".//div[@class=\"badge_title\"]");
                var name = WebUtility.HtmlDecode(nameNode.FirstChild.InnerText).Trim();

                var cardNode = badge.SelectSingleNode(".//span[@class=\"progress_info_bold\"]");
                var cards = cardNode == null ? Empty : Regex.Match(cardNode.InnerText, @"[0-9]+").Value;

                var badgeInMemory = AllBadges.FirstOrDefault(b => b.StringId == appId);
                if (badgeInMemory != null)
                {
                    badgeInMemory.UpdateStats(cards, hours);
                }
                else
                {
                    AllBadges.Add(new Badge(appId, name, cards, hours));
                }
            }
        }

        public async Task CheckCardDrops(Badge badge)
        {
            if (!await badge.CanCardDrops())
            {
                NextIdle();
            }
            else
            {
                // Resets the clock based on the number of remaining drops
                TimeLeft = badge.RemainingCard == 1 ? 300 : 900;
            }

            lblCurrentRemaining.Text = badge.RemainingCard + " " + localization.strings.card_drops_remaining;
            pbIdle.Value = pbIdle.Maximum - badge.RemainingCard;
            lblHoursPlayed.Text = badge.HoursPlayed + " " + localization.strings.hrs_on_record;
            UpdateStateInfo();
        }

        public FrmMain()
        {
            InitializeComponent();
            AllBadges = new List<Badge>();
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            // Copy external references to the output directory.  This allows ClickOnce install.
            if (File.Exists(Environment.CurrentDirectory + "\\steam_api.dll") == false)
            {
                CopyResource("IdleMaster.Resources.steam_api.dll", Environment.CurrentDirectory + @"\steam_api.dll");
            }
            if (File.Exists(Environment.CurrentDirectory + "\\CSteamworks.dll") == false)
            {
                CopyResource("IdleMaster.Resources.CSteamworks.dll",
                    Environment.CurrentDirectory + @"\CSteamworks.dll");
            }
            if (File.Exists(Environment.CurrentDirectory + "\\steam-idle.exe") == false)
            {
                CopyResource("IdleMaster.Resources.steam-idle.exe", Environment.CurrentDirectory + @"\steam-idle.exe");
            }

            // Update the settings, if needed.  When the application updates, settings will persist.
            if (Settings.Default.updateNeeded)
            {
                Settings.Default.Upgrade();
                Settings.Default.updateNeeded = false;
                Settings.Default.Save();
            }

            // Set the interface language from the settings
            if (Settings.Default.language != "")
            {
                var language_string = "";
                switch (Settings.Default.language)
                {
                    case "Bulgarian":
                        language_string = "bg";
                        break;
                    case "Chinese (Simplified, China)":
                        language_string = "zh-CN";
                        break;
                    case "Chinese (Traditional, China)":
                        language_string = "zh-TW";
                        break;
                    case "Czech":
                        language_string = "cs";
                        break;
                    case "Danish":
                        language_string = "da";
                        break;
                    case "Dutch":
                        language_string = "nl";
                        break;
                    case "English":
                        language_string = "en";
                        break;
                    case "Finnish":
                        language_string = "fi";
                        break;
                    case "French":
                        language_string = "fr";
                        break;
                    case "German":
                        language_string = "de";
                        break;
                    case "Greek":
                        language_string = "el";
                        break;
                    case "Hungarian":
                        language_string = "hu";
                        break;
                    case "Italian":
                        language_string = "it";
                        break;
                    case "Japanese":
                        language_string = "ja";
                        break;
                    case "Korean":
                        language_string = "ko";
                        break;
                    case "Norwegian":
                        language_string = "no";
                        break;
                    case "Polish":
                        language_string = "pl";
                        break;
                    case "Portuguese":
                        language_string = "pt-PT";
                        break;
                    case "Portuguese (Brazil)":
                        language_string = "pt-BR";
                        break;
                    case "Romanian":
                        language_string = "ro";
                        break;
                    case "Russian":
                        language_string = "ru";
                        break;
                    case "Spanish":
                        language_string = "es";
                        break;
                    case "Swedish":
                        language_string = "sv";
                        break;
                    case "Thai":
                        language_string = "th";
                        break;
                    case "Turkish":
                        language_string = "tr";
                        break;
                    case "Ukrainian":
                        language_string = "uk";
                        break;
                    default:
                        language_string = "en";
                        break;
                }
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(language_string);
            }

            // Localize form elements
            fileToolStripMenuItem.Text = localization.strings.file;
            gameToolStripMenuItem.Text = localization.strings.game;
            helpToolStripMenuItem.Text = localization.strings.help;
            settingsToolStripMenuItem.Text = localization.strings.settings;
            blacklistToolStripMenuItem.Text = localization.strings.blacklist;
            exitToolStripMenuItem.Text = localization.strings.exit;
            pauseIdlingToolStripMenuItem.Text = localization.strings.pause_idling;
            resumeIdlingToolStripMenuItem.Text = localization.strings.resume_idling;
            skipGameToolStripMenuItem.Text = localization.strings.skip_current_game;
            blacklistCurrentGameToolStripMenuItem.Text = localization.strings.blacklist_current_game;
            statisticsToolStripMenuItem.Text = localization.strings.statistics;
            changelogToolStripMenuItem.Text = localization.strings.release_notes;
            officialGroupToolStripMenuItem.Text = localization.strings.official_group;
            aboutToolStripMenuItem.Text = localization.strings.about;
            lnkSignIn.Text = "(" + localization.strings.sign_in + ")";
            lnkResetCookies.Text = "(" + localization.strings.sign_out + ")";
            toolStripStatusLabel1.Text = localization.strings.next_check;
            toolStripStatusLabel1.ToolTipText = localization.strings.next_check;

            lblSignedOnAs.Text = localization.strings.signed_in_as;
            GamesState.Columns[0].Text = localization.strings.name;
            GamesState.Columns[1].Text = localization.strings.hours;

            // Set the form height
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.625;
            Height = Convert.ToInt32(scale);

            // Set the location of certain elements so that they scale correctly for different DPI settings
            var point = new Point(Convert.ToInt32(graphics.DpiX * 1.14), Convert.ToInt32(lblGameName.Location.Y));
            lblGameName.Location = point;
            point = new Point(Convert.ToInt32(graphics.DpiX * 2.35), Convert.ToInt32(lnkSignIn.Location.Y));
            lnkSignIn.Location = point;
            point = new Point(Convert.ToInt32(graphics.DpiX * 2.15), Convert.ToInt32(lnkResetCookies.Location.Y));
            lnkResetCookies.Location = point;
        }

        private void FrmMain_FormClose(object sender, FormClosedEventArgs e)
        {
            StopIdle();
        }

        private void TmrCheckCookieData_Tick(object sender, EventArgs e)
        {
            var connected = !IsNullOrWhiteSpace(Settings.Default.sessionid) &&
                            !IsNullOrWhiteSpace(Settings.Default.steamLogin);

            lblCookieStatus.Text = connected
                ? localization.strings.idle_master_connected
                : localization.strings.idle_master_notconnected;
            lblCookieStatus.ForeColor = connected ? Color.Green : Color.Black;
            picCookieStatus.Image = connected ? Resources.imgTrue : Resources.imgFalse;
            lnkSignIn.Visible = !connected;
            lnkResetCookies.Visible = connected;
            IsCookieReady = connected;
        }

        private void TmrCheckSteam_Tick(object sender, EventArgs e)
        {
            var isSteamRunning = SteamAPI.IsSteamRunning() || Settings.Default.ignoreclient;
            lblSteamStatus.Text = isSteamRunning
                ? (Settings.Default.ignoreclient
                    ? localization.strings.steam_ignored
                    : localization.strings.steam_running)
                : localization.strings.steam_notrunning;
            lblSteamStatus.ForeColor = isSteamRunning ? Color.Green : Color.Black;
            picSteamStatus.Image = isSteamRunning ? Resources.imgTrue : Resources.imgFalse;
            tmrCheckSteam.Interval = isSteamRunning ? 5000 : 500;
            skipGameToolStripMenuItem.Enabled = isSteamRunning;
            pauseIdlingToolStripMenuItem.Enabled = isSteamRunning;
            IsSteamReady = isSteamRunning;
        }

        private void LblGameName_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://store.steampowered.com/app/" + CurrentBadge.AppId);
        }

        private void LnkResetCookies_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ResetClientStatus();
        }

        /// <summary>
        /// Performs reset to initial state
        /// </summary>
        private void ResetClientStatus()
        {
            // Clear the settings
            Settings.Default.sessionid = Empty;
            Settings.Default.steamLogin = Empty;
            Settings.Default.myProfileURL = Empty;
            Settings.Default.steamparental = Empty;
            Settings.Default.Save();

            // Stop the steam-idle process
            StopIdle();

            // Clear the badges list
            AllBadges.Clear();

            // Resize the form
            var graphics = CreateGraphics();
            var scale = graphics.DpiY * 1.625;
            Height = Convert.ToInt32(scale);

            // Set timer intervals
            tmrCheckSteam.Interval = 500;
            tmrCheckCookieData.Interval = 500;

            // Hide signed user name
            if (Settings.Default.showUsername)
            {
                lblSignedOnAs.Text = Empty;
                lblSignedOnAs.Visible = false;
            }

            // Hide spinners
            picReadingPage.Visible = false;

            // Hide lblDrops and lblIdle
            lblDrops.Visible = false;
            lblIdle.Visible = false;

            // Set IsCookieReady to false
            IsCookieReady = false;

            // Re-enable tmrReadyToGo
            tmrReadyToGo.Enabled = true;
        }

        private void LnkSignIn_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var frm = new FrmBrowser();
            frm.ShowDialog();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void TmrReadyToGo_Tick(object sender, EventArgs e)
        {
            if (!IsCookieReady || !IsSteamReady)
            {
                return;
            }

            // Update the form elements
            if (Settings.Default.showUsername)
            {
                lblSignedOnAs.Text = SteamProfile.GetSignedAs();
                lblSignedOnAs.Visible = true;
            }

            lblDrops.Visible = true;
            lblDrops.Text = localization.strings.reading_badge_page + ", " + localization.strings.please_wait;
            lblIdle.Visible = false;
            picReadingPage.Visible = true;

            tmrReadyToGo.Enabled = false;

            // Call the loadBadges() function asynchronously
            await LoadBadgesAsync();

            StartIdle();
        }


        private async void TmrCardDropCheck_Tick(object sender, EventArgs e)
        {
            if (TimeLeft <= 0)
            {
                tmrCardDropCheck.Enabled = false;
                if (CurrentBadge != null)
                {
                    CurrentBadge.Idle();
                    await CheckCardDrops(CurrentBadge);
                }

                var isMultipleIdle = CanIdleBadges.Any(b => !Equals(b, CurrentBadge) && b.InIdle);
                if (isMultipleIdle)
                {
                    await LoadBadgesAsync();
                    UpdateIdleProcesses();

                    isMultipleIdle = CanIdleBadges.Any(b => b.HoursPlayed < 2 && b.InIdle);
                    if (isMultipleIdle)
                    {
                        TimeLeft = 360;
                    }
                }

                // Check if user is authenticated and if any badge left to idle
                // There should be check for IsCookieReady, but property is set in timer tick, so it could take some time to be set.
                tmrCardDropCheck.Enabled = !IsNullOrWhiteSpace(Settings.Default.sessionid) && IsSteamReady &&
                                           CanIdleBadges.Any() && TimeLeft != 0;
            }
            else
            {
                TimeLeft = TimeLeft - 1;
                lblTimer.Text = TimeSpan.FromSeconds(TimeLeft).ToString(@"mm\:ss");
            }
        }

        private void BtnSkip_Click(object sender, EventArgs e)
        {
            if (!IsSteamReady)
            {
                return;
            }

            StopIdle();
            AllBadges.RemoveAll(b => Equals(b, CurrentBadge));
            StartIdle();
        }

        private void BtnPause_Click(object sender, EventArgs e)
        {
            if (!IsSteamReady)
            {
                return;
            }

            // Stop the steam-idle process
            StopIdle();

            // Indicate to the user that idling has been paused
            lblCurrentStatus.Text = localization.strings.idling_paused;

            // Set the correct button visibility
            btnResume.Visible = true;
            btnPause.Visible = false;
            pauseIdlingToolStripMenuItem.Enabled = false;
            resumeIdlingToolStripMenuItem.Enabled = true;

            // Focus the resume button
            btnResume.Focus();
        }

        private void BtnResume_Click(object sender, EventArgs e)
        {
            // Resume idling
            StartIdle();

            pauseIdlingToolStripMenuItem.Enabled = true;
            resumeIdlingToolStripMenuItem.Enabled = false;
        }

        private void SettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Show the form
            var previous = Settings.Default.sort;
            var previousBehavior = Settings.Default.OnlyOneGameIdle;
            var previousBehavior2 = Settings.Default.OneThenMany;
            Form frm = new FrmSettings();
            frm.ShowDialog();

            if (previous != Settings.Default.sort || previousBehavior != Settings.Default.OnlyOneGameIdle ||
                previousBehavior2 != Settings.Default.OneThenMany)
            {
                StopIdle();
                AllBadges.Clear();
                tmrReadyToGo.Enabled = true;
            }

            if (!Settings.Default.showUsername || !IsCookieReady)
            {
                return;
            }
            lblSignedOnAs.Text = SteamProfile.GetSignedAs();
            lblSignedOnAs.Visible = Settings.Default.showUsername;
        }

        private void PauseIdlingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnPause.PerformClick();
        }

        private void ResumeIdlingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnResume.PerformClick();
        }

        private void SkipGameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            btnSkip.PerformClick();
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new FrmAbout();
            frm.ShowDialog();
        }

        private void FrmMain_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
            {
                if (WindowState == FormWindowState.Normal)
                {
                    notifyIcon1.Visible = false;
                }
            }
            else
            {
                if (!Settings.Default.minToTray)
                {
                    return;
                }
                notifyIcon1.Visible = true;
                Hide();
            }
        }

        private void NotifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void lblCurrentRemaining_Click(object sender, EventArgs e)
        {
            if (TimeLeft > 2)
            {
                TimeLeft = 2;
            }
        }

        private void BlacklistToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new FrmBlacklist();
            frm.ShowDialog();

            if (Settings.Default.blacklist.Cast<string>().Any(appid => appid == CurrentBadge.StringId))
            {
                btnSkip.PerformClick();
            }
        }

        private void BlacklistCurrentGameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.blacklist.Add(CurrentBadge.StringId);
            Settings.Default.Save();

            btnSkip.PerformClick();
        }

        private void TmrStartNext_Tick(object sender, EventArgs e)
        {
            tmrStartNext.Enabled = false;
            StartIdle();
        }

        private void ChangelogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new FrmChangelog();
            frm.Show();
        }

        private void StatisticsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var frm = new FrmStatistics(_statistics);
            frm.ShowDialog();
        }

        private void OfficialGroupToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://steamcommunity.com/groups/idlemastery");
        }

        private void TmrBadgeReload_Tick(object sender, EventArgs e)
        {
            ReloadCount = ReloadCount + 1;
            lblDrops.Text = localization.strings.badge_didnt_load.Replace("__num__", (10 - ReloadCount).ToString());

            if (ReloadCount == 10)
            {
                tmrBadgeReload.Enabled = false;
                tmrReadyToGo.Enabled = true;
                ReloadCount = 0;
            }
        }

        private void TmrStatistics_Tick(object sender, EventArgs e)
        {
            _statistics.increaseMinutesIdled();
            _statistics.checkCardRemaining((uint) CardsRemaining);
        }
    }
}