using System;
using Sage.Entity.Interfaces;

namespace FX.MailQueue
{
    public class Rules
    {
        public static void OnCreate(IMailQueue mailqueue)
        {
            mailqueue.IsHtml = true;
        }

        public static void MailQueueProcessedStep(IMailQueue mailqueue)
        {
        }
    }
}
