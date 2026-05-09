namespace TenantStoreApi.Domain.Entities;

//this is for tenant to tenant invitation
public class TenantDelegation : BaseEntity
{
    public Guid HostTenantId { get; set; }
    public Guid GuestTenantId { get; set; }
    public string GuestTenantName  { get; set; }
    public string AccessLevel { get; set; } = "";
}

//1. Use Unique Public Identifiers(Tenant Codes)
//Never let users search by the Tenant's Name or Email. Instead, generate a random, non-sequential string for every tenant upon registration.
//Internal ID: Guid(e.g., 550e8400-e29b...) — Used for Foreign Keys.
//Public Code: String (e.g., TX-998-AZP) — Used for finding partners.
//The Workflow:
//Tenant B (the Guest) goes to their settings and clicks "Show my Connection Code."
//Tenant B sends this code to Tenant A via a secure private channel(Email, Slack, etc.).
//Tenant A enters the code.The system does not say "Tenant Found: Microsoft." It simply says "Request Sent."
//Tenant B must then Accept the request in their own dashboard to reveal the connection.
//2. Implement a "Discovery" Toggle
//In your Tenant table, add a boolean column for privacy settings.
//C#
//public class Tenant : BaseEntity
//{
//    public string Name { get; set; }
//    public string PublicCode { get; set; } // e.g. "TR-7721"

//    // Security Toggles
//    public bool IsSearchable { get; set; } = false;
//    public bool AllowDelegationRequests { get; set; } = true;
//}
//Default Off: New tenants are "invisible" by default.
//Manual Entry: If IsSearchable is false, the only way to find them is to have their exact PublicCode.
//3. The "Request-Response" Loop
//To prevent "brute-forcing" codes(where a script tries random codes to see which ones are valid), implement these three layers:
//Rate Limiting: If a User / IP fails to find a valid Tenant Code 3 times in a row, block their ability to search for an hour.
//Generic Feedback: When a code is entered, the UI should say: "An invitation has been sent to the owner of that code (if it exists)." This prevents an attacker from knowing if the code was actually valid.
//The "Inbox" Pattern: The Guest Tenant gets a notification: "Tenant 'Jhoncee Delivery' wants to delegate access to you. [Accept] [Ignore]".
//4. Architecture Logic for the Search Service
//When you write the search logic in C#, ensure you are using a strictly filtered query.
//C#
//public async Task<bool> RequestDelegation(string code, Guid requesterTenantId)
//{
//    // 1. Find the target based on the PUBLIC CODE, not name
//    var target = await _context.Tenants
//        .FirstOrDefaultAsync(t => t.PublicCode == code && t.AllowDelegationRequests);

//    if (target == null) return false; // Return false regardless of why it failed

//    // 2. Create the record with a "Pending" status
//    _context.TenantDelegations.Add(new TenantDelegation
//    {
//        HostTenantId = requesterTenantId,
//        GuestTenantId = target.Id,
//        Status = DelegationStatus.Pending // Require Guest to approve
//    });
//    await _context.SaveChangesAsync();
//    return true;
//}
//Summary of Best Practices
//Obfuscation: Use TenantCode instead of TenantName.
//Consent: No link is active until the Guest clicks "Approve."
//Anonymity: The Host should not see the Guest's real data until the Guest accepts the "Handshake."


//To keep your system professional and secure, you should use a collision - resistant, human-readable generator.You want codes that are easy to type over a phone or email(avoiding similar-looking characters like O and 0 or I and L) but long enough that they can't be guessed.Here is a robust utility for your C# backend:1. The Tenant Code GeneratorThis uses a "Base32" style alphabet (excluding confusing characters) to generate codes like BRT2-99XP.C#public static class TenantCodeGenerator
//{
//    // Characters that are distinct and easy to read (Excluded: 0, 1, O, I, L)
//    private static readonly char[] _alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ".ToCharArray();
//private static readonly Random _random = new Random();

//public static string Generate(int segmentLength = 4)
//{
//    return $"{GetRandomString(segmentLength)}-{GetRandomString(segmentLength)}";
//}

//private static string GetRandomString(int length)
//{
//    var result = new char[length];
//    for (int i = 0; i < length; i++)
//    {
//        result[i] = _alphabet[_random.Next(_alphabet.Length)];
//    }
//    return new string(result);
//}
//}
//2.The "Blind Handshake" WorkflowTo prevent exposure, your UI and API should follow this secure pattern:StepActionSecurity Benefit1. GenerationGuest clicks "Enable Partnering" and gets a code (e.g., FG82-H12P).No public directory exists.2.SharingGuest sends the code to the Host privately.Out-of-band verification.3. SubmissionHost enters the code in your app.API only returns Success (Request Sent), never the Tenant's name.4. VerificationGuest sees a "Pending Request" from "Host Name".The Guest (the one being searched) gets the final say.3. Implementation in your Controller/ServiceWhen the Host submits the code, don't return the Tenant object. Just create the "Pending" delegation.C#public async Task<string> CreateDelegationRequest(string inputCode, Guid hostTenantId)
//{
//    // 1. Search for the code
//    var guestTenant = await _context.Tenants
//        .FirstOrDefaultAsync(t => t.PublicCode == inputCode.ToUpper());

//// 2. Security: Always return the same message, even if code is wrong
//if (guestTenant == null)
//{
//    return "If a tenant with that code exists, a request has been sent.";
//}

//// 3. Create the 'Pending' record
//var request = new TenantDelegation
//{
//    HostTenantId = hostTenantId,
//    GuestTenantId = guestTenant.Id,
//    AccessLevel = "PendingApproval",
//    IsActive = false
//};

//_context.TenantDelegations.Add(request);
//await _context.SaveChangesAsync();

//return "Request sent successfully.";
//}
//Pro Tip for your HRMS:If you are worried about "Code Guessing" (Brute Force), you can add a CodeExpiry column to your Tenant table. The code only stays valid for 24 hours, or the Admin can click "Refresh Code" to invalidate the old one immediately.
