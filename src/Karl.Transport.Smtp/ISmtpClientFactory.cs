namespace Karl.Transport.Smtp;

internal interface ISmtpClientFactory
{
    ISmtpClientAdapter Create();
}
