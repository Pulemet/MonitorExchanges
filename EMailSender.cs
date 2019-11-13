using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Xml.Serialization;
using IdxEditor.Rendering.Attributes;
using QuantOffice.Execution;

public class EMailSender : Disposable
{
    private sealed class Mail : Disposable
    {
        internal readonly MailSenderParameters Parameters;
        internal readonly string Id = Guid.NewGuid().ToString();
        internal readonly string Subject, Body;
        internal readonly string[] Attachments;
        internal int Attempt;
        internal SmtpClient Client;
        internal MailMessage MailMessage;
        internal StrategyTimer ResendTimer;
        internal bool IsSentAsync;

        internal Mail(MailSenderParameters parameters, string subject, string body, params string[] attachments)
        {
            Parameters = (MailSenderParameters) parameters.Clone();
            Subject = subject;
            Body = body;
            Attachments = attachments;
        }

        public override string ToString()
        {
            return String.Format("Mail ID: {1}{0}" +
                                 "Subject: {2}{0}" +
                                 "Body:{0}" +
                                 "---{0}" +
                                 "{3}{0}" +
                                 "---{0}" +
                                 "Attachments:{0}" +
                                 "\t{4}",
                Environment.NewLine, Id, Subject, Body,
                (Attachments != null && Attachments.Length > 0)
                    ? string.Join(Environment.NewLine + "\t", Attachments)
                    : "There are no attachments");
        }

        protected override void Dispose(bool disposing)
        {
            if (IsSentAsync && Client != null)
            {
                Client.SendAsyncCancel();
            }

            IsSentAsync = false;
            Dispose(ref ResendTimer);
            Dispose(ref MailMessage);
            Dispose(ref Client);
        }
    }

    private readonly MailSenderParameters parametersByDefault;
    private readonly Dictionary<string, Mail> mails = new Dictionary<string, Mail>();
    private readonly string TEST_SUBJECT;
    internal PortfolioExecutor PortfolioExecutor;
    private const string SUBJECT_PREFIX = "C2.io Alert!! ";

    static EMailSender()
    {
        ServicePointManager.ServerCertificateValidationCallback =
            (sender, certificate, chain, sslPolicyErrors) => true;
    }

    public EMailSender(PortfolioExecutor portfolioExecutor, MailSenderParameters parameters)
    {
        if (portfolioExecutor == null)
        {
            throw new ArgumentNullException("portfolioExecutor");
        }

        if (parameters == null)
        {
            throw new ArgumentNullException("parameters");
        }

        parametersByDefault = parameters.Clone();
        PortfolioExecutor = portfolioExecutor;
        TEST_SUBJECT = string.Concat("Test message from '", PortfolioExecutor.RunInfo.StrategyName, "' module.");
    }

    public void SendTest()
    {
        if (!parametersByDefault.Enabled)
        {
            PortfolioExecutor.Log(
                "Can't send test e-mail! Reason: Sending e-mails is disabled in the input parameters.'");
            return;
        }

        string body = string.Concat("This message was sent automatically by '",
            PortfolioExecutor.RunInfo.StrategyName, "' module when checking the account settings.");
        Send(TEST_SUBJECT, body);
    }

    public void Send(string subject, string body, IEnumerable<string> attachments = null)
    {
        Send(parametersByDefault, subject, body, attachments);
    }

    public void Send(string subject, string body, params string[] attachments)
    {
        Send(parametersByDefault, subject, body, attachments);
    }

    public void Send(MailSenderParameters parameters, string subject, string body,
        IEnumerable<string> attachments = null)
    {
        Send(parameters, subject, body, (attachments != null) ? attachments.ToArray() : null);
    }

    public void Send(MailSenderParameters parameters, string subject, string body, params string[] attachments)
    {
        if (parameters == null)
        {
            throw new ArgumentNullException("parameters");
        }

        lock (SynchObj)
        {
            if (isDisposed || parameters == null || !parameters.Enabled)
            {
                return;
            }

            body += string.Format("{0}{0}---{0}Strategy: {1}{0}Time (UTC): {2}.", Environment.NewLine,
                PortfolioExecutor.RunInfo.StrategyName,
                Utils.TimeInString(PortfolioExecutor.CurrentTime));
            Mail mail = new Mail(parameters, SUBJECT_PREFIX + subject, body, attachments);
            mails[mail.Id] = mail;
            SendMailCallback(mail);
        }
    }


    private void SendMailCallback(object state)
    {
        lock (SynchObj)
        {
            if (isDisposed)
            {
                return;
            }

            Mail mail = (Mail) state;
            mail.Attempt++;
            Dispose(ref mail.ResendTimer);
            Dispose(ref mail.MailMessage);
            Dispose(ref mail.Client);
            try
            {
                // creating SMTP client
                mail.Client =
                    new SmtpClient(mail.Parameters.Host, mail.Parameters.Port); // SMTP server authentication 
                mail.Client.Credentials = new NetworkCredential(mail.Parameters.UserName, mail.Parameters.Password,
                    mail.Parameters.Domain);
                mail.Client.EnableSsl = mail.Parameters.EnableSsl;
                mail.Client.ServicePoint.MaxIdleTime = mail.Parameters.MaxIdleTime;
                //mail.client.ServicePoint.ConnectionLimit = 1;

                // creating mail message
                mail.MailMessage = new MailMessage();
                if (!string.IsNullOrEmpty(mail.Parameters.From))
                {
                    mail.MailMessage.From = new MailAddress(mail.Parameters.From);
                }

                AddAddresses(mail.MailMessage.To, mail.Parameters.To);
                AddAddresses(mail.MailMessage.CC, mail.Parameters.CC);
                AddAddresses(mail.MailMessage.Bcc, mail.Parameters.BCC);
                mail.MailMessage.Subject = mail.Subject;
                mail.MailMessage.Body = mail.Body;
                if (mail.Attachments != null) // adding attachments if any
                {
                    for (int i = 0; i < mail.Attachments.Length; i++)
                    {
                        string file = mail.Attachments[i];
                        if (string.IsNullOrEmpty(file))
                        {
                            continue;
                        }

                        try
                        {
                            file = Path.GetFullPath(file.Trim());
                            FileInfo fileInfo = new FileInfo(file);
                            Attachment attachment = new Attachment(fileInfo.FullName);
                            // add file information
                            attachment.ContentDisposition.CreationDate = fileInfo.CreationTime;
                            attachment.ContentDisposition.ModificationDate = fileInfo.LastWriteTime;
                            attachment.ContentDisposition.ReadDate = fileInfo.LastAccessTime;
                            mail.MailMessage.Attachments.Add(attachment); // add a file as message attachment
                        }
                        catch (Exception ex)
                        {
                            PortfolioExecutor.Log(String.Format(
                                "Can't add attachment! Mail ID: {1}; Error: {2}; Attachment: {3}{0}{4}",
                                Environment.NewLine, mail.Id, ex.Message, file, ex));
                        }
                    }
                }

                // send e-mail
                mail.Client.SendCompleted += OnMailSentAsync;
                mail.IsSentAsync = true;
                mail.Client.SendAsync(mail.MailMessage, mail);
            }
            catch (Exception exception)
            {
                SentError(mail, exception);
            }
        }
    }

    private void OnMailSentAsync(object sender, AsyncCompletedEventArgs args)
    {
        Mail mail = (Mail) args.UserState;
        mail.IsSentAsync = false;
        SmtpClient client = mail.Client;
        if (client != null)
        {
            client.SendCompleted -= OnMailSentAsync;
        }

        try
        {
            PortfolioExecutor.ExecutionServer.CallUnderLock(OnMailSent, args);
        }
        catch (Exception exception)
        {
            Debug.Assert(false, exception.ToString());
        }
    }

    private object OnMailSent(AsyncCompletedEventArgs args)
    {
        lock (SynchObj)
        {
            if (isDisposed)
            {
                return null;
            }

            Mail mail = (Mail) args.UserState;
            if (args.Error != null)
            {
                SentError(mail, args.Error);
            }
            else
            {
                if (mail.Subject == TEST_SUBJECT)
                {
                    PortfolioExecutor.Log("Test e-mail has been sent successfully.");
                }

                mails.Remove(mail.Id);
            }

            return null;
        }
    }

    private void SentError(Mail mail, Exception exception)
    {
        if (mail.Subject == TEST_SUBJECT)
        {
            PortfolioExecutor.Log(String.Format("Can't send test e-mail!  Error: {0}", exception));
            mails.Remove(mail.Id);
            return;
        }

        if (mail.Attempt <= mail.Parameters.MaxAttempts)
        {
            PortfolioExecutor.Log(String.Format(
                "Can't send at this time! Next attempt will be in {1} minute(s)...{0}{2}{0}---{0}{3}",
                Environment.NewLine, mail.Parameters.ResendInterval, this, exception));
            mail.ResendTimer = SetTimer(TimeSpan.FromMinutes(mail.Parameters.ResendInterval), SendMailCallback,
                mail);
        }
        else
        {
            PortfolioExecutor.Log(String.Format("Can't send e-mail '{1}'.{0}{2}{0}---{0}{3}", Environment.NewLine,
                mail.Subject, this, exception));
            mails.Remove(mail.Id);
        }
    }

    internal StrategyTimer SetTimer(TimeSpan interval, TimerCallback onTimer, object state)
    {
        StrategyTimer timer = PortfolioExecutor.Timers.CreateTimer(interval, onTimer, state);
        timer.Start();
        return timer;
    }

    private void AddAddresses(MailAddressCollection target, string addresses)
    {
        addresses = (addresses ?? string.Empty).Trim();
        if (addresses.Length == 0)
        {
            return;
        }

        foreach (string address in addresses.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => !string.IsNullOrEmpty(item)))
        {
            try
            {
                target.Add(new MailAddress(address));
            }
            catch (Exception)
            {
                PortfolioExecutor.Log(String.Format("'{0}' is not valid e-mail address! Will be ignored.",
                    address));
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        foreach (Mail mail in mails.Values)
        {
            if (mail.Client != null)
            {
                mail.Client.SendCompleted -= OnMailSentAsync;
            }

            mail.Dispose();
        }

        mails.Clear();
    }
}


[Serializable]
public sealed class MailSenderParameters : Parameters<MailSenderParameters>
{
    #region Variables

    [DisplayInfo(DisplayName = "Enable?")] public bool Enabled;

    [DisplayInfo(DisplayName = "Host or IP:", Group = "SMTP Server")]
    public string Host = "Host or IP";

    [DisplayInfo(DisplayName = "Port:", Group = "SMTP Server")]
    public int Port = 25;

    [DisplayInfo(DisplayName = "User Name:", Group = "Credentials")]
    public string UserName = "User";

    [DisplayInfo(DisplayName = "Password:", Group = "Credentials")]
    public string Password = "password";

    [DisplayInfo(DisplayName = "Domain:", Group = "Credentials")]
    public string Domain = string.Empty;

    [DisplayInfo(DisplayName = "Enable SSL?", Group = "SMTP Client")]
    public bool EnableSsl;

    // without this the connection is idle too long and not terminated, times out at the server and gives sequencing errors
    [DisplayInfo(DisplayName = "Max Idle Time (ms):", Group = "SMTP Client")]
    public int MaxIdleTime = 10000;

    [DisplayInfo(DisplayName = "From:", Group = "Mail")]
    public string From = "Name <e-mail>";

    [DisplayInfo(DisplayName = "To:", Group = "Mail")]
    public string To = "Name <e-mail>; ...";

    [DisplayInfo(DisplayName = "CC:", Group = "Mail")]
    public string CC = "Name 1 <e-mail>; Name 2 <e-mail>; ...";

    [DisplayInfo(DisplayName = "BCC:", Group = "Mail")]
    public string BCC = "Name 1 <e-mail>; Name 2 <e-mail>; ...";

    [DisplayInfo(DisplayName = "Max Attempts To Resend:")]
    public int MaxAttempts = 5;

    [DisplayInfo(DisplayName = "Resend Interval (mins):")]
    public int ResendInterval = 5;

    #endregion

    #region BuildingBlocks

    internal override void CopyDataFrom(MailSenderParameters source)
    {
        if (source == null)
        {
            throw new ArgumentNullException("source");
        }

        Enabled = source.Enabled;
        Host = source.Host;
        Port = source.Port;
        UserName = source.UserName;
        Password = source.Password;
        Domain = source.Domain;
        EnableSsl = source.EnableSsl;
        MaxIdleTime = source.MaxIdleTime;
        From = source.From;
        To = source.To;
        CC = source.CC;
        BCC = source.BCC;
        MaxAttempts = source.MaxAttempts;
        ResendInterval = source.ResendInterval;
    }

    internal static bool ValidateAddresses(PortfolioExecutor portfolioExecutor, ref string addressesDefinition,
        string title, string name, ref bool isValid)
    {
        if (addressesDefinition == null || (addressesDefinition = addressesDefinition.Trim()).Length == 0)
        {
            return false;
        }

        List<string> addresses = addressesDefinition.Split(new char[] {';'}, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => !string.IsNullOrEmpty(item)).ToList();
        if (addresses.Count == 0)
        {
            isValid = false;
            portfolioExecutor.Log(String.Format("'{0} -> {1}' is not valid!", title, name));
            return false;
        }

        ValidateAddresses(portfolioExecutor, addresses, title, name, ref isValid);
        return true;
    }

    internal static void ValidateAddresses(PortfolioExecutor portfolioExecutor, IEnumerable<string> addresses,
        string title, string name, ref bool isValid)
    {
        foreach (string address in addresses)
        {
            try
            {
                new MailAddress(address);
            }
            catch (Exception)
            {
                isValid = false;
                portfolioExecutor.Log(String.Format("'{0} -> {1}' is not valid!", title, name));
            }
        }
    }

    internal override string ToString(string prefix)
    {
        return String.Format("{1}Enabled: {2}{0}" +
                             "{1}SMTP Server -> Host or IP: {3}{0}" +
                             "{1}SMTP Server -> Port: {4}{0}" +
                             "{1}Credentials -> User Name: {5}{0}" +
                             "{1}Credentials -> Password: {6}{0}" +
                             "{1}Credentials -> Domain: {7}{0}" +
                             "{1}SMTP Client -> Enable SSL: {8}{0}" +
                             "{1}SMTP Client -> Max Idle Time (ms): {9}{0}" +
                             "{1}Mail -> From: {10}{0}" +
                             "{1}Mail -> To: {11}{0}" +
                             "{1}Mail -> CC: {12}{0}" +
                             "{1}Mail -> BCC: {13}{0}" +
                             "{1}Max Attempts To Resend: {14}{0}" +
                             "{1}Resend Interval (mins): {15}",
            Environment.NewLine, prefix, Enabled, Host, Port,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(UserName)),
            (string.IsNullOrWhiteSpace(Password) ? string.Empty : "********"),
            Domain, EnableSsl, MaxIdleTime, From, To, CC, BCC, MaxAttempts, ResendInterval);
    }

    #endregion
}

[Serializable]
public abstract class Parameters<T>
    where T : Parameters<T>, new()
{
    #region BuildingBlocks

    internal virtual T Clone()
    {
        T result = new T();
        result.CopyDataFrom((T) this);
        return result;
    }

    internal abstract void CopyDataFrom(T source);

    internal abstract string ToString(string prefix);

    public override string ToString()
    {
        return ToString(string.Empty).Replace(Environment.NewLine, "; ");
    }

    #endregion
}

public abstract class Disposable : IDisposable
{
    #region Variables

    /// <summary>
    ///     The object to synchronization.
    /// </summary>
    [XmlIgnore] [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public readonly object SynchObj;

    /// <summary>
    ///     Value indicating whether this object is disposed.
    /// </summary>
    [XmlIgnore] [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected bool isDisposed;

    /// <summary>
    ///     Gets a value indicating whether this object is disposed.
    /// </summary>
    /// <value>
    ///     <c>true</c> this object has been disposed; otherwise, <c>false</c>.
    /// </value>
    [XmlIgnore]
    [DebuggerHidden]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsDisposed
    {
        [DebuggerStepThrough] get { return isDisposed; }
    }

    #endregion

    #region BuildingBlocks

    public Disposable()
    {
        SynchObj = new object();
    }

    /// <summary>
    ///     Releases unmanaged resources and performs other cleanup operations before the <see cref="Disposable" /> is
    ///     reclaimed by garbage collection.
    /// </summary>
    [DebuggerStepThrough]
    ~Disposable()
    {
        lock (SynchObj)
        {
            if (isDisposed)
            {
                return;
            }

            try
            {
                Dispose(false);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
            }

            isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    ///     Checks if this object is disposed.
    /// </summary>
    /// <exception cref="Exception">If object is disposed.</exception>
    [DebuggerStepThrough]
    [Conditional("DEBUG")]
    public void CheckIsDisposed()
    {
        lock (SynchObj)
        {
            if (isDisposed)
            {
                throw new Exception("Object is disposed!");
            }
        }
    }

    /// <summary>
    ///     Disposes the specified object.
    /// </summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="obj">The object.</param>
    [DebuggerStepThrough]
    public static void Dispose<T>(T obj)
        where T : class, IDisposable
    {
        Dispose(ref obj);
    }

    /// <summary>
    ///     Disposes the specified object.
    /// </summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="obj">The object.</param>
    [DebuggerStepThrough]
    public static void Dispose<T>(ref T obj)
        where T : class, IDisposable
    {
        if (obj != null)
        {
            obj.Dispose();
            obj = null;
        }
    }

    /// <summary>
    ///     Disposes the specified object.
    /// </summary>
    /// <param name="obj">The object.</param>
    [DebuggerStepThrough]
    public static void Dispose(object obj)
    {
        Dispose(ref obj);
    }

    /// <summary>
    ///     Disposes the specified object.
    /// </summary>
    /// <param name="obj">The object.</param>
    [DebuggerStepThrough]
    public static void Dispose(ref object obj)
    {
        IDisposable disposable = obj as IDisposable;
        if (disposable != null)
        {
            disposable.Dispose();
        }

        obj = null;
    }

    /// <summary>
    ///     Stops and disposes the specified <see cref="StrategyTimer" />.
    /// </summary>
    /// <param name="obj">The <see cref="StrategyTimer" />.</param>
    public static void Dispose(ref StrategyTimer obj)
    {
        if (obj != null && !obj.IsStopped)
        {
            obj.Stop();
            obj = null;
        }
    }

    /// <summary>
    ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    [DebuggerStepThrough]
    public void Dispose()
    {
        lock (SynchObj)
        {
            if (isDisposed)
            {
                return;
            }

            try
            {
                Dispose(true);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception.ToString());
            }

            isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    ///     Releases unmanaged and, optionally, managed resources.
    /// </summary>
    /// <param name="disposing">
    ///     <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only
    ///     unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>
    ///     Returns a <see cref="System.String" /> that represents this instance.
    /// </summary>
    /// <returns>
    ///     A <see cref="System.String" /> that represents this instance.
    /// </returns>
    [DebuggerStepThrough]
    public override string ToString()
    {
        lock (SynchObj)
        {
            return ((isDisposed) ? "Object is disposed!" : "Object is not disposed.");
        }
    }

    #endregion
}
