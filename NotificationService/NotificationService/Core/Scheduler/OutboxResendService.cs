//using Microsoft.Extensions.Hosting;
//using Onepunch.Common.Lib.Services;
//using OnePunch.Notification.Core.Services;

//namespace OnePunch.Notification.Core.Scheduler;

//public class OutboxResendService : BackgroundService
//{
//    private readonly OutBoxService _service;

//    public OutboxResendService(OutBoxService service)
//    {
//        _service = service;
//    }
//    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//    {
//        //while (!stoppingToken.IsCancellationRequested)
//        //{
//        //    //GET all processing state and beyong expiry that needs to be resend
//        //    //exclude only failed and processed
//        //    var pendingMessages = OutboxRepository.GetPendingMessages();
//        //    foreach (var msg in pendingMessages)
//        //    {
//        //        try
//        //        {
//        //            using var smtp = new SmtpClient("smtp.example.com")
//        //            {
//        //                Credentials = new System.Net.NetworkCredential("me@example.com", "password")
//        //            };

//        //            var mail = new MailMessage("me@example.com", msg.To, msg.Subject, msg.Body);
//        //            smtp.Send(mail);

//        //            OutboxRepository.UpdateStatus(msg.Id, "Sent");
//        //        }
//        //        catch (Exception ex)
//        //        {
//        //            OutboxRepository.UpdateStatus(msg.Id, "Failed");
//        //            Console.WriteLine($"Error sending: {ex.Message}");
//        //        }
//        //    }

//        //    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
//        //}
//    }
//}