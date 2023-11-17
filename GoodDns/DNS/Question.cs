
namespace GoodDns.DNS
{
    public class Question
    {
        public string domainName;
        public ushort questionType;
        public ushort questionClass;

        public Question(string domainName, RTypes qType, RClasses qClass)
        {
            this.domainName = domainName;
            this.questionType = (ushort)qType;
            this.questionClass = (ushort)qClass;
        }

        public void Print()
        {
            //print the packet
            Console.WriteLine("Domain Name: " + domainName);
            Console.WriteLine("Question Type: " + Enum.GetName(typeof(RTypes), questionType));
            Console.WriteLine("Question Class: " + Enum.GetName(typeof(RClasses), questionClass));
        }
        public string GetDomainName()
        {
            //get the domain name
            return domainName;
        }

        public ushort GetQType()
        {
            //get the question type
            return questionType;
        }

        public ushort GetQClass()
        {
            //get the question class
            return questionClass;
        }
    }
}