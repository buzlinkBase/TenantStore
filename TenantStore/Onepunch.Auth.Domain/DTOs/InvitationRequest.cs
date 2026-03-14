using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Domain.DTOs;

public class InvitationRequest
{
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    [Required]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public required string Email { get; set; }
}