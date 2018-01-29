using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Mail;
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
            SmtpPort = 25;
            SmtpUseSSL = false;
            SmtpDefaultFromAddress = "none@none.com";
        }

        #region SMTP Settings
        [DisplayName("SMTP Server"), Description("The SMTP server address")]
        public string SmtpServer { get; set; }
        [DisplayName("SMTP User Name"), Description("The SMTP user to authenticate with")]
        public string SmtpUser { get; set; }
        [DisplayName("SMTP Password"), Description("The password for the SMTP User")]
        public string SmtpPassword { get; set; }

        [DisplayName("SMTP Port"), Description("The SMTP server port. Default is 25")]
        public int SmtpPort { get; set; } = 25;
        [DisplayName("SMTP Use SSL"), Description("Whether to use SSL for the SMTP connection")]
        public bool SmtpUseSSL { get; set; }

        [DisplayName("Default From Address"), Description("Indicates the default from address if not provided on the MailQueue record.")]
        public string SmtpDefaultFromAddress { get; set; }
        #endregion 

        protected override void OnExecute()
        {
            SetProgress("Initializing");

            if (!ValidateSettings())
            {
                const string invalidMsg = "The MailQueue job settings are invalid. Configure the job to set SMTP settings";
                SetProgress("Error", invalidMsg);
                log.Error(invalidMsg);
            }

            var mailList = EntityFactory.GetRepository<IMailQueue>().FindAll();

            var current = 0;
            var errors = 0;
            var total = mailList.Count;

            foreach (var mailItem in mailList)
            {
                current++;
                SetProgress("Processing", "Processing E-mail Queue", current, total);

                try
                {
                    var mail = new MailMessage();

                    mail.From = new MailAddress(mailItem.FromAddress ?? SmtpDefaultFromAddress);
                    mail.To.Add(mailItem.ToAddress);
                    mail.Subject = mailItem.Subject;
                    mail.Body = mailItem.Body;
                    mail.IsBodyHtml = mailItem.IsHtml ?? false;

                    if (string.IsNullOrEmpty(mailItem.AttachmentPath) && File.Exists(mailItem.AttachmentPath))
                        mail.Attachments.Add(new Attachment(mailItem.AttachmentPath));

                    var smtp = new SmtpClient(SmtpServer, SmtpPort);

                    if (!string.IsNullOrEmpty(SmtpUser))
                        smtp.Credentials = new NetworkCredential(SmtpUser, SmtpPassword);

                    smtp.EnableSsl = SmtpUseSSL;

                    smtp.Send(mail);

                }
                catch (Exception ex)
                {
                    mailItem.ErrorResult = "Error: " + ex.Message;
                    mailItem.Save();

                    errors++;
                    log.Error("Error e-mail for MailQueue ID " + mailItem.Id, ex);
                    continue;
                }

                mailItem.ErrorResult = string.Empty;
                mailItem.MailQueueProcessed();

                if (!string.IsNullOrEmpty(mailItem.RecordForContactId))
                    RecordForContact(mailItem);

                mailItem.Delete();
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
                string.IsNullOrEmpty(SmtpServer) 
                || string.IsNullOrEmpty(SmtpUser) 
                || string.IsNullOrEmpty(SmtpPassword)
            );
        }
    }
}
