using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WebCrawler
{
    /// <summary>
    /// Класс для сбора ссылок
    /// </summary>
    public class LinkGrabberManager
    {
        LinkStorage storage = new LinkStorage();
        List<LinkGrabber> tasks = new List<LinkGrabber>();
        /// <summary>
        /// Хранилище найденных адресов
        /// </summary>
        public LinkStorage Storage
        {
            get { return storage; }
        }
        private int taskCount = 1;
        /// <summary>
        /// Количество потоков, выделяемых для поиска ссылок.
        /// </summary>
        public int TaskCount
        {
            get { return taskCount; }
        }

        private int searchLevel = -1;
        /// <summary>
        /// Глубина поиска. -1 - поиск без ограничений. 0 - только первый уровень.
        /// </summary>
        public int CrawlLevel
        {
            get { return searchLevel; }
        }

        /// <summary>
        /// Событие, уведомляющее о том, что в хранилище добавлен новый адрес. Первый аргумент - адрес, второй - идентификатор потока, в котором он обнаружен
        /// </summary>
        public event Action<string, int> NewLinkFound;
        /// <summary>
        /// Событие завершения поиска
        /// </summary>
        public event Action Finished;
        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="taskCount">Количество задач (потоков) для сбора ссылок</param>
        /// <param name="searchLevel">Глубина поиска</param>
        public LinkGrabberManager(int taskCount, int searchLevel)
        {
            if (taskCount > 0)
                this.taskCount = taskCount;
            if (searchLevel > -1)
                this.searchLevel = searchLevel;

            storage.NewLinkFound += Storage_NewLinkFound;
        }

        private void Storage_NewLinkFound(string link, int threadId)
        {
            //Пробросим событие на урвень выше.
            NewLinkFound?.Invoke(link, threadId);
        }

        /// <summary>
        /// Запустить поиск
        /// </summary>
        public void StartSearch(string url)
        {
            StopSearch();
            storage.Clear();

            finished = false;
            string content = LinkGrabber.GetContent(url);
            int? _searchLevel = null;
            if (searchLevel > -1)
                _searchLevel = searchLevel;
            for (int i = 0; i < taskCount; i++)
            {
                var task = new LinkGrabber(storage);
                task.Finished += Task_Finished;
                tasks.Add(task);
                task.Start(url, content, _searchLevel);
            }
        }

        private void Task_Finished()
        {
            //Завершенные задачи можно "натравлять" на обработанные адреса ещё выполняющихся задач. Но для простоты не будем этим заниматься сейчас.
            if (tasks.All(t => t.IsFinished) && !finished)
            {
                finished = true;                
                Finished?.Invoke();
            }
        }

        private bool finished = false;
        /// <summary>
        /// Остановить поиск
        /// </summary>
        public void StopSearch()
        {
            foreach (var task in tasks)
                task.Stop();
            tasks.Clear();
        }
    }

    /// <summary>
    /// Класс для хранения собранных ссылок
    /// </summary>
    public class LinkStorage
    {
        ConcurrentDictionary<string, int> resultLinksDict = new ConcurrentDictionary<string, int>();
        /// <summary>
        /// Словарь собранных ссылок (в данном словаре все ссылки абсолютные). Ключ - идентификатор потока, в котором обнаружен адрес.
        /// </summary>
        public ConcurrentDictionary<string, int> LinksDict
        {
            get { return resultLinksDict; }
        }

        /// <summary>
        /// Добавить ссылку в хранилище
        /// </summary>
        /// <param name="link">Ссылка</param>
        /// <param name="task">Экземпляр сборщика, получившего ссылку</param>
        /// <returns>Успешность добавления. Если False, значит ссылка уже присутствует в хранилище</returns>
        public bool TryAddLink(string link)
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            if (resultLinksDict.TryAdd(link.ToLower(), threadId))
            {
                NewLinkFound?.Invoke(link, threadId);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Очистить хранилище
        /// </summary>
        public void Clear()
        {
            resultLinksDict.Clear();
        }
        /// <summary>
        /// Событие, уведомляющее о том, что в хранилище добавлен новый адрес
        /// </summary>
        public event Action<string, int> NewLinkFound;
    }
    /// <summary>
    /// Сборщик, работающий в отдельном потоке
    /// </summary>
    public class LinkGrabber
    {

        /// <summary>
        /// Конструктор
        /// </summary>
        /// <param name="storage"></param>        
        public LinkGrabber(LinkStorage storage)
        {
            this.storage = storage;
        }

        private Guid taskId = Guid.NewGuid();
        /// <summary>
        /// Уникальный идентификатор задачи
        /// </summary>
        public Guid TaskId
        {
            get { return taskId; }
        }
        /// <summary>
        /// Хранилище ссылок
        /// </summary>
        LinkStorage storage;

        CancellationTokenSource cancellationTokenSource;
        Task task;

        private bool isFinshed;
        /// <summary>
        /// Признак звершенности задачи поиска
        /// </summary>
        public bool IsFinished
        {
            get { return isFinshed; }
            private set { isFinshed = value; }
        }


        private void OnFinished()
        {
            IsFinished = true;
            Finished?.Invoke();
        }
        /// <summary>
        /// Событие завершения работы сборщика. 
        /// </summary>
        public event Action Finished;

        /// <summary>
        /// Запустить поиск ссылок
        /// </summary>
        /// <param name="address">Html страницы, на которой необходимо искать ссылки</param>
        /// <param name="pageContent">Содержимое страницы. Если null, то будет считано по адресу. Нужно для того, чтобы на первом уровне не производить одно и то же действие получения содержимого для каждой задачи.</param>
        /// <param name="searchLevel">Уровень вложенности поиска. Если null, то поиск без ограничений. Нумерация начинается с 0.</param>
        public void Start(string address, string pageContent, int? searchLevel = null)
        {
            Stop();
            cancellationTokenSource = new CancellationTokenSource();
            IsFinished = false;
            task = new Task(() =>
            {
                try
                {
                    DoCrawlLinks(address, pageContent, searchLevel, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    //Отмена поиска вполне легальная операция, ничего делать не надо
                }
                catch (Exception ex)
                {
                    //Необработанная ошибка
                    Console.WriteLine(string.Format("Error: {0}", ex.Message));
                }
                finally
                {
                    OnFinished();
                }
            }, cancellationTokenSource.Token);
            task.Start();
        }

        /// <summary>
        /// Остановить сбор ссылок
        /// </summary>
        public void Stop()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;
        }

        private void DoCrawlLinks(string address, int? crawlLevel, CancellationToken? cancellationToken)
        {
            DoCrawlLinks(address, null, crawlLevel, cancellationToken);
        }

        private void DoCrawlLinks(string baseAddress, string pageContent, int? searchLevel, CancellationToken? cancellationToken)
        {
            if (searchLevel < 0)
                return;
            cancellationToken?.ThrowIfCancellationRequested();

            //Если содержимое не задано, попытаемся его получить
            if (pageContent == null)
                pageContent = GetContent(baseAddress);

            //Найдем все вхождения ссылок внутри содержимого страницы с помощью соответствующего регулярного выражения
            Regex regexLink = new Regex("(?<=<a\\s*?href=(?:'|\"))[^'\"@]*(?=(?:'|\"))");
            foreach (var match in regexLink.Matches(pageContent))
            {
                var link = match.ToString();
                //Преобразуем относительный адрес в абсолютный, если это необходимо
                string absoluteAddress = GetAbsoluteAddress(baseAddress, link);
                var address2 = GetAbsoluteAddress(absoluteAddress, absoluteAddress);

                cancellationToken?.ThrowIfCancellationRequested();
                //Добавим обнаруженную ссылку в хранилище
                if (storage.TryAddLink(absoluteAddress))
                {
                    cancellationToken?.ThrowIfCancellationRequested();
                    //Если ссылка успешно добавлена в хранилище, рекурсивно вызовем сбор ссылок в содержимом страницы, соответствующей этой ссылке
                    int? nextLevel = null;
                    if (searchLevel != null)
                        nextLevel = searchLevel - 1;
                    DoCrawlLinks(absoluteAddress, nextLevel, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Преобразование абсолютного пути в относительный, если это необходимо
        /// </summary>
        /// <param name="relativeUrl"></param>
        /// <returns></returns>
        private string GetAbsoluteAddress(string baseUrl, string relativeUrl)
        {
            return new Uri(new Uri(baseUrl), relativeUrl).AbsoluteUri;
        }

        /// <summary>
        /// Получить html страницы по адресу
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static string GetContent(string address)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    return client.DownloadString(address);
                }                    
            }
            catch (Exception ex)
            {
                //Если не удалось получить содержимое таблицы, то просто проигнорируем её.
                //TODO Тут можно организовать уведомление об ошибке
                return string.Empty;
            }
        }
    }
}
