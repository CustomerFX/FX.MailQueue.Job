using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Xml;
using Quartz;
using Sage.Entity.Interfaces;
using Sage.Platform;
using Sage.Platform.Scheduling;

namespace FX.MailQueue
{
    [DisallowConcurrentExecution]
    [DisplayName("Mail Queue")]
    [Description("Infor CRM E-mail Queue from Customer FX")]
    public class Job : SystemJobBase
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Job() : base()
        {
            Enabled = true;
            SmtpPort = 25;
            SmtpUseSSL = false;
            DefaultFromAddress = "no-reply@email.com";
        }

        #region Configuration Settings
        private bool Enabled { get; set; }
        private string SmtpServer { get; set; }
        private string SmtpUser { get; set; }
        private string SmtpPassword { get; set; }
        private int SmtpPort { get; set; } = 25;
        private bool SmtpUseSSL { get; set; }
        private string DefaultFromAddress { get; set; }
        #endregion 

        protected override void OnExecute()
        {
            LoadConfiguration();
            if (!Enabled) return;

            SetProgress("Initializing");
            
            if (!ValidateSettings())
            {
                const string invalidMsg = "The MailQueue job configuration is invalid. Edit the FXMailQueue.config in the job portal to set SMTP settings.";
                SetProgress("Error", invalidMsg);
                log.Error(invalidMsg);
                return;
            }

            var mailQueue = EntityFactory.GetRepository<IMailQueue>().FindAll();

            var current = 0;
            var errors = 0;
            var total = mailQueue.Count;

            foreach (var queueItem in mailQueue)
            {
                current++;
                SetProgress("Processing", "Processing E-mail Queue", current, total);

                try
                {
                    var mail = new MailMessage();

                    mail.From = new MailAddress((string.IsNullOrEmpty(queueItem.FromAddress) ? queueItem.FromAddress : DefaultFromAddress));
                    mail.To.Add(queueItem.ToAddress);
                    mail.Subject = queueItem.Subject;
                    mail.Body = queueItem.Body;
                    mail.IsBodyHtml = queueItem.IsHtml ?? false;

                    if (string.IsNullOrEmpty(queueItem.AttachmentPath) && File.Exists(queueItem.AttachmentPath))
                        mail.Attachments.Add(new Attachment(queueItem.AttachmentPath));

                    var smtp = new SmtpClient(SmtpServer, SmtpPort);

                    if (!string.IsNullOrEmpty(SmtpUser))
                        smtp.Credentials = new NetworkCredential(SmtpUser, SmtpPassword);

                    smtp.EnableSsl = SmtpUseSSL;

                    smtp.Send(mail);

                }
                catch (Exception ex)
                {
                    queueItem.ErrorResult = "Error: " + ex.Message;
                    queueItem.Save();

                    errors++;
                    log.Error("Error e-mail for MailQueue ID " + queueItem.Id, ex);
                    continue;
                }

                queueItem.ErrorResult = string.Empty;
                queueItem.MailQueueProcessed();

                if (!string.IsNullOrEmpty(queueItem.RecordForContactId))
                    RecordForContact(queueItem);

                queueItem.Delete();
            }

            log.Info("Processed " + total + " E-mails");
            SetProgress((errors == 0 ? "Complete" : "Error"), "Job complete. Processed " + (total-errors) + " e-mails with " + errors + " errors.", 100, 100);
        }

        private void RecordForContact(IMailQueue mail)
        {
            if (string.IsNullOrEmpty(mail.RecordForContactId)) return;

            var contact = EntityFactory.GetById<IContact>(mail.RecordForContactId);
            if (contact == null) return;

            var user = EntityFactory.GetById<IUser>(mail.CreateUser);

            var note = EntityFactory.Create<IHistory>();
            note.AccountId = contact.Account.Id.ToString();
            note.AccountName = contact.Account.AccountName;
            note.ContactId = contact.Id.ToString();
            note.ContactName = contact.FullName;
            note.Type = HistoryType.atEMail;
            note.UserId = user?.Id.ToString() ?? "ADMIN";
            note.UserName = user?.UserInfo.NameLF ?? "Administrator";
            note.Description = mail.Subject;
            note.Result = "Completed";
            note.StartDate = DateTime.Now;
            note.Timeless = true;
            note.Duration = 1;
            note.Category = "E-mail";
            note.CompletedDate = note.StartDate;
            note.CompletedUser = user?.Id.ToString() ?? "ADMIN";
            note.Notes = (mail.Body.Length > 255 ? mail.Body.Substring(0, 255) : mail.Body);
            note.LongNotes = mail.Body;
            note.Save();
        }

        private void SetProgress(string PhaseName = null, string PhaseDescription = null, int? Current = null, int? Total = null)
        {
            if (!string.IsNullOrEmpty(PhaseName)) this.Phase = PhaseName;
            if (!string.IsNullOrEmpty(PhaseDescription)) this.PhaseDetail = PhaseDescription;
            if (Current.HasValue && Total.HasValue) this.Progress = (Total.Value == 0 ? 0 : (((decimal)Current.Value / (decimal)Total.Value) * (decimal)100));
        }

        private bool ValidateSettings()
        {
            return (
                !string.IsNullOrEmpty(SmtpServer) 
                && !string.IsNullOrEmpty(SmtpUser) 
                && !string.IsNullOrEmpty(SmtpPassword)
            );
        }

        private void LoadConfiguration()
        {
            try
            {
                var config = new XmlDocument();
                config.Load(ConfigFile);

                Enabled = Convert.ToBoolean(config.SelectSingleNode("FXMailQueue/Enabled").InnerText);
                SmtpServer = config.SelectSingleNode("FXMailQueue/SmtpServer").InnerText;
                SmtpUser = config.SelectSingleNode("FXMailQueue/SmtpUser").InnerText;
                SmtpPassword = config.SelectSingleNode("FXMailQueue/SmtpPassword").InnerText;
                SmtpPort = Convert.ToInt32(config.SelectSingleNode("FXMailQueue/SmtpPort").InnerText);
                SmtpUseSSL = Convert.ToBoolean(config.SelectSingleNode("FXMailQueue/SmtpUseSSL").InnerText);
                DefaultFromAddress = config.SelectSingleNode("FXMailQueue/DefaultFromAddress").InnerText;
            }
            catch (Exception ex)
            {
                log.Error("Error loading configuration file " + ConfigFile, ex);
            }
        }

        private static string ConfigFile
        {
            get
            {
                var codeBase = Assembly.GetExecutingAssembly().CodeBase;
                var path = Path.GetDirectoryName(Uri.UnescapeDataString(new UriBuilder(codeBase).Path));
                return Path.Combine(Directory.GetParent(path).FullName, "FXMailQueue.config");
            }
        }
    }
}
