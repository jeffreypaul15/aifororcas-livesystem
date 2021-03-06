using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using NotificationSystem.Models;
using NotificationSystem.Utilities;

namespace NotificationSystem
{
    [StorageAccount("OrcaNotificationStorageSetting")]
    public static class SubscriberEmail
    {
        [FunctionName("SubscriberEmail")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", "delete")] HttpRequest req,
            [Table("EmailList")] CloudTable cloudTable,
            ILogger log)
        {
            return await EmailHelpers.UpdateEmailList<SubscriberEmailEntity>(req, cloudTable, log);
        }
    }
}
