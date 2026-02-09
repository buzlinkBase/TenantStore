using Adms.api.Domain.Entties;

namespace Adms.api.Domain.ValueObjects
{
    public class AttendanceModel
    {
        public Guid Id { get; set; }
        public int BioId { get; set; }
        public DateTime WorkDateTime { get; set; }
        public string SN { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public virtual int Workstate { get; set; }
        public virtual string Verifycode { get; set; } = string.Empty;
        public string BatchCode { get; set; } = string.Empty;

    }
} 