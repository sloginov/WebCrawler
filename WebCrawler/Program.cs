using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebCrawler
{
    class Program
    {
        static void Main(string[] args)
        {
            //Запрос количества используемых потоков (задач)
            int taskCount = RequestNumber("Input thread count...");
            //Запрос глубины поиска
            int searchLevel = RequestNumber("Input search level...");            
            //Запрос адреса для поиска
            string url = RequestUrl("Input url for searching...");

            //Далее инициализируем сборщик и подпишемся на события обнаружения новых адресов и окончания поиска для вывода этой информации на экран
            LinkGrabberManager linkGrabber = new LinkGrabberManager(taskCount, searchLevel);
            linkGrabber.NewLinkFound += (string address, int threadId) =>
            {
                //Помимо адреса выведем на экран идентификатор потока, в котором был обнаружен адрес.
                Console.WriteLine("#{0} - {1}", threadId, address);
            };
            linkGrabber.Finished += () => 
            {                
                Console.WriteLine("Searching is finished! {0} addresses found", linkGrabber.Storage.LinksDict.Count);
            };
            linkGrabber.StartSearch(url);
            Console.ReadLine();
        }

        private static void LinkGrabber_Finished()
        {
            Console.WriteLine("Searching is finished!");
        }

        private static void LinkGrabber_NewLinkFound(string obj)
        {
            throw new NotImplementedException();
        }

        private static int RequestNumber(string message)
        {
            while (true)
            {
                Console.WriteLine(message);
                try
                {
                    return Int32.Parse(Console.ReadLine());
                }
                catch
                {
                    Console.WriteLine("Value must be a number");
                }
            }
        }

        private static string RequestUrl(string message)
        {
            while (true)
            {
                Console.WriteLine(message);
                try
                {
                    var str = Console.ReadLine().ToLower();
                    if (!str.StartsWith("http://") && !str.StartsWith("https://"))
                        str = "http://" + str;
                    var url = new Uri(str);                    
                    return url.AbsoluteUri;
                }
                catch
                {
                    Console.WriteLine("Inputed string is not valid URL");
                }
            }
        }
    }
}
