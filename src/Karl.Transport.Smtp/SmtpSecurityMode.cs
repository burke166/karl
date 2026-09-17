namespace Karl.Transport.Smtp;

public enum SmtpSecurityMode
{
    StartTlsRequired,
    None,
    ImplicitTls,
    StartTlsWhenAvailable
}
