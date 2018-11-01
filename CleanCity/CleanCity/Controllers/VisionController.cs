using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace CleanCity.Controllers
{
    [Route("api/[controller]")]
    public class VisionController : Controller
    {
        private const string PredictionUrl = "https://southcentralus.api.cognitive.microsoft.com/customvision/v2.0/Prediction/PROJET_ID/";
        private const string UrlPredictinPath = "url?iterationId=ITERATION_ID";
        private const string ImagePredictionPath = "image?iterationId=ITERATION_ID";
        private const string UploadPath = "https://cleancityimages.azurewebsites.net/uploads/";
        private static HttpClient client;
        private readonly IHostingEnvironment environment;

        public VisionController(IHostingEnvironment env)
        {
            client = new HttpClient();
            client.BaseAddress = new Uri(PredictionUrl);
            client.DefaultRequestHeaders.Add("Prediction-Key", "PREDICTION_KEY");
            environment = env;
        }

        // GET api/values
        [HttpGet]
        public IEnumerable<Record> Get()
        {
            var records = new List<Record>();
            using (ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("168.62.30.94,ssl=false,password=I@mMrOwlAdmin"))
            {
                IDatabase db = redis.GetDatabase();
                var r = db.HashGetAll("GARBAGE-RECORDS");
                foreach (var item in r)
                {
                    records.Add(JsonConvert.DeserializeObject<Record>(item.Value.ToString()));
                }
            }

            return records;
        }

        // GET api/values/5
        [HttpGet("{id}")]
        public async Task<Prediction> Get(string id)
        {
            var content = new StringContent(JsonConvert.SerializeObject(new { Url = $"{UploadPath}{id}" }));
            content.Headers.Clear();
            content.Headers.Add("Content-Type", "application/json");
            var predictionresult = await client.PostAsync(UrlPredictinPath, content);
            var predictionString = await predictionresult.Content.ReadAsStringAsync();
            var prediction = JsonConvert.DeserializeObject<PredictionResult>(predictionString);
            var predictions = prediction.predictions;
            return predictions.OrderBy(p => p.probability).Last();
        }

        [HttpPost("UploadFiles")]
        public async Task<string> Post(IFormFile file, float latitude, float longitude)
        {
            if (file.Length > 0)
            {
                var fileName = file.FileName;
                var uniqueName = Guid.NewGuid().ToString();
                var uniqueFileName = uniqueName + Path.GetExtension(fileName);
                var fullUniqueFileName = Path.Combine(environment.WebRootPath, uniqueFileName);
                using (FileStream fs = System.IO.File.Create(fullUniqueFileName))
                {
                    file.CopyTo(fs);
                    fs.Flush();
                }

                var testUrl = $"{Request.Scheme}://{Request.Host.Value}/{uniqueFileName}";
                var content = new StringContent(JsonConvert.SerializeObject(new { Url = testUrl }));
                content.Headers.Clear();
                content.Headers.Add("Content-Type", "application/json");
                var predictionresult = await client.PostAsync(UrlPredictinPath, content);
                var predictionString = await predictionresult.Content.ReadAsStringAsync();
                var prediction = JsonConvert.DeserializeObject<PredictionResult>(predictionString);
                var predictions = prediction.predictions;
                var maxProbability = predictions.OrderBy(p => p.probability).Last();
                if (maxProbability.tagName == "Garbage")
                {
                    using (ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("127.0.0.1"))
                    {
                        var r = new Record { image = uniqueFileName, lat = latitude, lng = longitude };
                        IDatabase db = redis.GetDatabase();
                        var hashFileds = new HashEntry[] { new HashEntry(r.image, JsonConvert.SerializeObject(r)) };
                        db.HashSet("GARBAGE-RECORDS", hashFileds);
                    }
                    return "Garbage detected, Complaint recorded.";
                }
                else
                {
                    return "Not recognized as garbage.";
                }
            }

            return null;
        }
    }

    public class Prediction
    {
        public float probability { get; set; }
        public string tagName { get; set; }
    }

    public class PredictionResult
    {
        public List<Prediction> predictions { get; set; }
    }

    public class Record
    {
        public float lat { get; set; }
        public float lng { get; set; }
        public string image { get; set; }
    }
}
