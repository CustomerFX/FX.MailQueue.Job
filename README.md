# FX.MailQueue.Job
A Job for Infor CRM that allows sending e-mails by simply adding records to a MailQueue table

## Installation
1. To install, simply download and install the bundle [Customer FX Mail Queue Job](https://github.com/CustomerFX/FX.MailQueue.Job/raw/master/deliverable/Customer%20FX%20Mail%20Queue%20Job.zip)
2. Once installed, open the Job Portal in Application Architect and view the schedule for FX.MailQueue.Job. 
3. Click the schedule's "Configure" button and enter your SMTP settings. 
4. Deploy the Job Service Portal

## Usage 
To use, simply add a record to the MailQueue table. This can be done directly in SQL or using the entity model. For example: 
```csharp
var mail = Sage.Platform.EntityModel.Create<Sage.Entity.Interfaces.IMailQueue>();
mail.ToAddress = "some@email.com"; // can be mulitple recipients, comma-delimited 
mail.FromAddress = "another@email.com";
mail.Subject = "Test Email";
mail.Body = "This is a test e-mail";

// optional, record as a note on a contact 
mail.RecordForContactId = "CXXXX0000001";

// optional, attach a file
mail.AttachmentPath = @"C:\SomeFolder\SomeFile.pdf";

mail.Save();
// now the mail will be sent
```

There is also an entity business rule on the MailQueue entity called `MailQueueProcessed`. This business rule will be executed for each e-mail processed, allowing you to add custom logic to the processing of each e-mail. 

Note: If an error occurs when sending the e-mail, the MailQueue record will be updated with the error details and the job will attempt to send it again on it's next execution. If the email is sent successfully, the MailQueue record will be deleted.
