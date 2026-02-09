//namespace OnePunch.Auth.Core;

//using hrms_api.Auth.Domain;
//using Microsoft.AspNetCore.Identity;

//public class NoOpEmailSender : IEmailSender<User>
//{
//    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink)
//    {
//        Console.WriteLine($"Confirmation link for {email}: {confirmationLink}");
//        return Task.CompletedTask;
//    }

//    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
//    {
//        Console.WriteLine($"Password reset link for {email}: {resetLink}");
//        return Task.CompletedTask;
//    }

//    public Task SendEmailAsync(User user, string email, string subject, string htmlMessage)
//    {
//        Console.WriteLine($"Email to {email}: {subject} - {htmlMessage}");
//        return Task.CompletedTask;
//    }

//    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
//    {
//        throw new NotImplementedException();
//    }
//}