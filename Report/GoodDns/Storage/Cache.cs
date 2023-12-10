using GoodDns.DNS;
using System.Threading.Tasks;

namespace GoodDns.Storage
{

    class Entry
    {
        public Question question;
        public Answer[] answers;
        public int timeToLive;
        public bool isExpired
        {
            get
            {
                return timeToLive > DateTimeOffset.Now.ToUnixTimeSeconds();
            }
        }

        public Entry(Packet packet)
        {
            //check if the packet contains an answer and a question
            if (packet.answerCount >= 1 && packet.questionCount >= 1)
            {
                //set the question
                question = packet.questions[0];
                //set the answers
                answers = packet.answers;
                //set the time to live
                timeToLive = ((int)packet.answers[0].ttl * 1000) + (int)DateTimeOffset.Now.ToUnixTimeSeconds();
            }
        }
    }

    public class Cache
    {
        List<Entry> entries = new List<Entry>();
        Task? cacheWorker;
        Settings settings;

        public Cache(Settings settings)
        {
            this.settings = settings;
        }

        public void addRecord(Packet packet)
        {
            //check if the packet contains an answer and a question
            if(packet.answers.Length >= 1 && packet.questions.Length >= 1) {
                //add the packet to the cache
                Entry entry = new Entry(packet);
                entries.Add(entry);
            }
        }

        public bool hasAnswer(Packet packet)
        {
            Question question = packet.questions[0];
            //loop through all entries
            for(int i = 0; i < entries.Count; i++) {
                //check if the entry matches the question
                if(entries[i].question.IsSame(question)) {
                    return true;
                }
            }
            return false;
        }

        public Answer[]? getAnswers(Packet packet) {
            Question question = packet.questions[0];
            //loop through all entries
            for(int i = 0; i < entries.Count; i++) {
                //check if the entry matches the question
                if(entries[i].question.IsSame(question)) {
                    return entries[i].answers;
                }
            }
            return null;
        }

        void Process()
        {
            //loop through all entries
            for (int i = 0; i < entries.Count; i++)
            {
                //check if the entry has expired
                if (entries[i].isExpired)
                {
                    //remove the entry
                    entries.RemoveAt(i);
                }
            }
        }

        public void Start()
        {
            cacheWorker = Task.Run(() => {
                while(true) {
                    Process();
                    //sleep for 0.1 second
                    Thread.Sleep(10);
                }
            });
        }

        public void Stop()
        {
            cacheWorker?.Dispose();
        }
    }
}