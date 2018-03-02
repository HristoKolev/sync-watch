namespace SyncWatch
{
    using System;
  

    public class Program
    {
        private static void Main()
        {
            // Set up session options
            SessionOptions sessionOptions = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = "vm3",
                UserName = "root",
                SshHostKeyFingerprint = "ssh-ed25519 256 M7hDz1mj12AngTbLl59DrCP+nnecAGtsudZsWVGmWCQ=",
            };

            using (Session session = new Session())
            {
                // Connect
                session.Open(sessionOptions);

                // Your code
            }

        }
    }
}