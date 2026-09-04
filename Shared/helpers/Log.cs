using System.IO;

namespace AlgoTrader.Shared.Logging
{
    public static class LogBase
    {
        public static void ClearLog(string filename)
        {

            try
            {
                using (FileStream fs = new FileStream(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "../../Logs", filename), FileMode.Truncate))
                {
                    fs.SetLength(0);
                }
            }
#pragma warning disable CS0168
            catch (Exception e)
            {
            }
        }

        public static string Write(string text, string filename, int maxLen = 102400)
        {
            text += "\n";

            try
            {
                using (FileStream fs = new FileStream(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "../../../Logs", filename), FileMode.OpenOrCreate))
                {
                    var len = fs.Length;

                    //get text as bytes
                    if (text.Length > maxLen)
                    {
                        text = text.Substring(0, maxLen);
                    }
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);

                    int textLen = bytes.Length;

                    if (len + textLen >= maxLen)
                    {
                        //truncate existing to < maxLen
                        int skip = (int)(len - maxLen + textLen); //how many bytes to skip
                        if (skip > len) skip = (int)len;
                        byte[] current = new byte[maxLen];
                        fs.Seek(skip, SeekOrigin.Begin); //read from offset until end
                        fs.Read(current, 0, (int)(len - skip));
                        bytes.CopyTo(current, len - skip); //append new data
                        fs.Seek(0, SeekOrigin.Begin); //return to start of file
                        fs.SetLength(maxLen);
                        fs.Write(current, 0, current.Length);//write
                        return System.Text.Encoding.Default.GetString(current);
                    }
                    else
                    {
                        //create a new file each time, with the new data appended
                        byte[] current = new byte[textLen + len];
                        fs.Seek(0, SeekOrigin.Begin);
                        fs.Read(current, 0, (int)(len));
                        bytes.CopyTo(current, len);
                        fs.Seek(0, SeekOrigin.Begin); //return to start of file
                        fs.Write(current, 0, current.Length);//write
                        return System.Text.Encoding.Default.GetString(current);
                    }
                }
            }
            catch (Exception e)
            {
                return "Logfile error " + e.ToString();
            }
        }

        internal static void StartSession()
        {
            LogConnection.WriteLine($"==============================================================================\nStarting session at {DateTime.Now}");
        }
    }

    //static classes can only derive from object, so not using a base class
    public static class LogConnection
    {
        public static void WriteLine(string text)
        {
            var s = LogBase.Write(text, "LogConnection.txt");
            MainObj.mainViewModel.DisplayLogConnection(s);
        }

        internal static void WriteLineTimestamp(string text)
        {
            try
            {
                var s = string.Format("{0}: {1}", DateTime.UtcNow.ToString(), text);
                s = LogBase.Write(s, "LogConnection.txt");
                MainObj.mainViewModel.DisplayLogConnection(s);
            }
            catch { }
        }
    }

    public static class LogExceptions
    {
        public static void WriteLine(string text)
        {
            LogBase.Write(text, "LogExceptions");
            LogConnection.WriteLine(text);
        }
    }

    public static class LogTrades
    {
        public static void WriteLine(string text)
        {
            var s = LogBase.Write(text, "LogTrades.txt");
            MainObj.mainViewModel.DisplayLogTrades(s);
        }

        //public static void WriteLineOrder(string text)
        //{
        //    LogBase.Write(text, "LogOrders.txt");
        //    WriteLine(text);
        //}

        public static void WriteSpacer()
        {
            WriteLine($"\n**************************************************************************************");
        }
    }

    public static class LogBackups
    {
        public static void WriteLine(string text)
        {
            LogBase.Write(text, "LogBackups.txt");
            LogConnection.WriteLine(text);

        }
    }
}
