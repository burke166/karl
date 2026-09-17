namespace Karl.Transport.Smtp;

public class SmtpTransportOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTlsRequired;
    public string? Username { get; set; }
    public string? Password { get; set; }
}
