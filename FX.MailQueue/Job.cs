using System;
using System.Collections.Generic;
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
using System.Linq;

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
        }

        #region Configuration Settings
        private bool Enabled { get; set; }
        private string SmtpServer { get; set; }
        private string SmtpUser { get; set; }
        private string SmtpPassword { get; set; }
        private int SmtpPort { get; set; } = 25;
        private bool SmtpUseSSL { get; set; }
        private string DefaultFromAddress { get; set; }
        private int MaxErrorAttempts { get; set; }
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
            var errorsDesc = string.Empty;
            var total = mailQueue.Count;

            foreach (var queueItem in mailQueue)
            {
                current++;
                SetProgress("Processing", "Processing E-mail Queue", current, total);

                // check for max error attempts
                if (MaxErrorAttempts > 0 && queueItem.ErrorAttempts.HasValue && queueItem.ErrorAttempts.Value > MaxErrorAttempts) continue;

                // check for delayed send
                if (queueItem.DelayUntil.HasValue && queueItem.DelayUntil.Value > DateTime.Now) continue;

                try
                {
                    using (var mail = new MailMessage())
                    {
                        mail.From = new MailAddress((!string.IsNullOrEmpty(queueItem.FromAddress) ? queueItem.FromAddress : DefaultFromAddress));

                        foreach (var addr in GetToAddressList(queueItem.ToAddress)) mail.To.Add(addr);

                        mail.Subject = queueItem.Subject;
                        mail.Body = queueItem.Body;
                        mail.IsBodyHtml = queueItem.IsHtml ?? false;

                        if (!string.IsNullOrEmpty(queueItem.AttachmentPath) && File.Exists(queueItem.AttachmentPath))
                            mail.Attachments.Add(new Attachment(queueItem.AttachmentPath));

                        var smtp = new SmtpClient(SmtpServer, SmtpPort);

                        if (!string.IsNullOrEmpty(SmtpUser))
                            smtp.Credentials = new NetworkCredential(SmtpUser, SmtpPassword);

                        smtp.EnableSsl = SmtpUseSSL;

                        smtp.Send(mail);
                    }

                    queueItem.ErrorResult = string.Empty;
                    queueItem.MailQueueProcessed();

                    if (!string.IsNullOrEmpty(queueItem.RecordForContactId))
                        RecordForContact(queueItem);

                    queueItem.Delete();

                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(errorsDesc)) errorsDesc += ", ";
                    errorsDesc += ex.Message + " (MailQueueID:" + queueItem + ")";
                    queueItem.ErrorResult = "Error: " + ex.Message;
                    queueItem.ErrorAttempts = (queueItem.ErrorAttempts + 1 ?? 1);
                    queueItem.Save();

                    errors++;
                    log.Error("Error e-mail for MailQueue ID " + queueItem.Id, ex);
                    continue;
                }
            }

            log.Info("Processed " + total + " E-mails");
            SetProgress((errors == 0 ? "Complete" : "Error"), "Job complete. Processed " + (total-errors) + " e-mails with " + errors + " errors." + (!string.IsNullOrEmpty(errorsDesc) ? " Errors: " + errorsDesc : string.Empty), 100, 100);
        }

        private IList<string> GetToAddressList(string ToAddress)
        {
            IList<string> list = new List<string>();
            if (string.IsNullOrEmpty(ToAddress)) return list;

            if (ToAddress.Contains(",")) list = ToAddress.Split(',');
            else if (ToAddress.Contains(";")) list = ToAddress.Split(';');
            else list.Add(ToAddress);

            return list.Select(x => x.Trim()).ToList();
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
            );
        }

        private void LoadConfiguration()
        {
            try
            {
                var config = new XmlDocument();
                config.Load(ConfigFile);

                Enabled = Convert.ToBoolean(GetConfigValue(config, "Enabled", true));
                SmtpServer = GetConfigValue(config, "SmtpServer", string.Empty);
                SmtpUser = GetConfigValue(config, "SmtpUser", string.Empty);
                SmtpPassword = GetConfigValue(config, "SmtpPassword", string.Empty);
                SmtpPort = Convert.ToInt32(GetConfigValue(config, "SmtpPort", 25));
                SmtpUseSSL = Convert.ToBoolean(GetConfigValue(config, "SmtpUseSSL", false));
                DefaultFromAddress = GetConfigValue(config, "DefaultFromAddress", "no-reply@fxmailqueue.com");
                MaxErrorAttempts = Convert.ToInt32(GetConfigValue(config, "MaxErrorAttempts", -1));
            }
            catch (Exception ex)
            {
                log.Error("Error loading configuration file " + ConfigFile, ex);
                throw;
            }
        }

        private string GetConfigValue(XmlDocument ConfigDoc, string Setting, object DefaultValue = null)
        {
            var value = ConfigDoc.SelectSingleNode("FXMailQueue/" + Setting)?.InnerText;
            return !string.IsNullOrEmpty(value) ? value : (DefaultValue?.ToString() ?? string.Empty);
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
