namespace GoodDns {
    public class Logging<T> {

        int logLevel;
        string logFile;
        Type caller;
        string callerName;

        public Logging(string logFile, int logLevel = 2) {
            this.logFile = logFile;
            this.caller = typeof(T);
            this.callerName = this.caller.Name;
            this.logLevel = logLevel;
        }

        public void Error(string message) {
            if (logLevel < 5) return;
            LogPrint(message, ConsoleColor.Red, "ERROR");
        }

        public void Warning(string message) {
            if (logLevel < 4) return;
            LogPrint(message, ConsoleColor.Yellow, "WARNING");
        }

        public void Debug(string message) {
            if (logLevel < 3) return;
            LogPrint(message, ConsoleColor.Cyan, "DEBUG");
        }

        public void Info(string message) {
            if (logLevel < 2) return;
            LogPrint(message, ConsoleColor.White, "INFO");
        }

        public void Success(string message) {
            if (logLevel < 1) return;
            LogPrint(message, ConsoleColor.Green, "SUCCESS");
        }

        private void LogPrint(string message, ConsoleColor color, string prefix = "") {
            Print(message, color, prefix);
            Log(message, prefix);
        }
        private string Padding(int len) {
            string padding = "";
            for (int i = 0; i < len; i++) {
                padding += " ";
            }
            return padding;
        }

        private void Print(string message, ConsoleColor color, string prefix = "") {
            Console.Write($"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{callerName}]{Padding(9 - callerName.Length)}");
            Console.ForegroundColor = color;
            Console.Write($"[{prefix}] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(message + "\n");
        }

        private void Log(string message, string prefix = "") {
            string logMessage = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] [{callerName}]{Padding(9 - callerName.Length)}[{prefix}] {message}";
            File.AppendAllText(logFile, logMessage + "\n");
        }
    }
}