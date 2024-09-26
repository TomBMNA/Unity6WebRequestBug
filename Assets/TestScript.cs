using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class TestScript : MonoBehaviour
{
    private const int MAX_CONCURRENT = 20;
    private const int BATCH_COUNT = 1000;
    private const string QUERY_ENDPOINT = "game_config.get_all";
    private const string BC_URL = "https://postchain-dev.myneighboralice.com/";
    
    [SerializeField] private bool _useWebRequests = true;
    
    private static int _activeCount;
    private static int _totalCount;

    private HttpClient _client;
    private string _bcBrid;

    async void Start()
    {
        await FetchBrid();
        
        if (!_useWebRequests)
        {
            ServicePointManager.DefaultConnectionLimit = MAX_CONCURRENT;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        
        StartSending(_useWebRequests);
    }

    private void OnDestroy()
    {
        _client?.Dispose();
    }

    async Task FetchBrid()
    {
        string bridRequestUrl = $"{BC_URL}brid/iid_0";
        var request = UnityWebRequest.Get(bridRequestUrl);
        await request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            throw new Exception($"Error fetching BRID: {request.error}");

        _bcBrid = request.downloadHandler.text;
        Debug.Log("BC Brid fetched: " + _bcBrid);
    }

    async void StartSending(bool useWebRequests)
    {
        List<Task<bool>> requestTasks = new List<Task<bool>>();
        bool hasFailed = false;

        while (!hasFailed)
        {
            requestTasks.Clear();
            for (int i = 0; i < BATCH_COUNT; i++)
            {
                bool shouldLog = i == 0; // Only log first query result
                var requestTask = useWebRequests ? SendSimpleQuery(QUERY_ENDPOINT, shouldLog) : SendHttp(QUERY_ENDPOINT, shouldLog);
                requestTasks.Add(requestTask);
            }

            await Task.WhenAll(requestTasks);

            hasFailed = requestTasks.Any(task => !task.Result);
            
            if (!hasFailed)
                Debug.Log($"Batch successfully completed. ActiveCount: {_activeCount} Total: {_totalCount}");
        }
        
        Debug.Log($"Error encountered after {_totalCount} requests, stopping");
    }

    async Task<bool> SendHttp(string query, bool shouldLogResult)
    {
        _totalCount++;
        _activeCount++;

        if (!Application.isPlaying)
        {
            return true;
        }

        try
        {
            var contentData = new StringContent(GetQuery(query), System.Text.Encoding.UTF8, "application/json");
            using (var response =
                   await _client.PostAsync(GetQueryBaseUrl(), contentData))
            {
                var content = response.Content.ReadAsStringAsync();
                var taskResult = content.Result;
                
                if (shouldLogResult)
                    Debug.Log($"Query result: {taskResult}");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
        finally
        {
            _activeCount--;
        }
        return true;
    }

    async Task<bool> SendSimpleQuery(string query, bool shouldLogResult)
    {
        _activeCount++;
        _totalCount++;
        string resultQuery = $"{GetQueryBaseUrl()}?type={query}";
        var request = new UnityWebRequest(resultQuery);
        var downloadHandler = new DownloadHandlerBuffer();
        request.downloadHandler = downloadHandler;
        request.disposeDownloadHandlerOnDispose = false;

        try
        {
            await request.SendWebRequest();
            
            if (!string.IsNullOrEmpty(request.error))
            {
                Debug.LogError(request.error);
                return false;
            }
            else
            {
                string content = request.downloadHandler.text;
                if (shouldLogResult)
                    Debug.Log($"Query result: {content}");
            }

            _activeCount--;
            
            return true;
        }
        finally
        {
            request?.Dispose();
            downloadHandler?.Dispose();
        }
    }

    string GetQueryBaseUrl()
    {
        return $"{BC_URL}/query/{_bcBrid}";
    }

    string GetQuery(string endpoint)
    {
        return "{\"type\":\"" + endpoint + "\"}";
    }
}
