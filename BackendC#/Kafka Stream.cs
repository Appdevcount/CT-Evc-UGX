// See https://aka.ms/new-console-template for more information
using Confluent.Kafka;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Streamiz.Kafka.Net;
using Streamiz.Kafka.Net.SerDes;
using System;
using ucx.messages.events;
using Ucx.ExternalSystemStatusChange.Translator;

const string readTopic = "Ucx.ExternalSystemStatusChange";
const string writeTopic = "Ucx.ExternalSystemStatusChangeHistorical";
CancellationTokenSource cancellationTokenSource = new();

var config = new StreamConfig<StringSerDes, StringSerDes>();
config.ApplicationId = "UCXKstream";

//Local
//config.ApplicationId = "UCXKstream";
//config.BootstrapServers = "localhost:9092";
//config.AutoOffsetReset = AutoOffsetReset.Earliest;
//config.Guarantee = ProcessingGuarantee.AT_LEAST_ONCE;
//config.SecurityProtocol = SecurityProtocol.Plaintext;
//config.SaslMechanism = SaslMechanism.Plain;
//config.SaslUsername = "admin";
//config.SaslUsername = "admin-secret";

config.AutoOffsetReset = AutoOffsetReset.Earliest;
config.Guarantee = ProcessingGuarantee.AT_LEAST_ONCE;
config.SecurityProtocol = SecurityProtocol.SaslSsl;
config.SaslMechanism = SaslMechanism.ScramSha512;
config.SaslUsername = "ksvcUCXACLService";

//DEV
config.BootstrapServers = "eheu2dv1cpkaf01.innovate.lan:9093,eheu2dv1cpkaf02.innovate.lan:9093,eheu2dv1cpkaf03.innovate.lan:9093";
config.SaslPassword = "";

//intg
//config.BootstrapServers = "eheu2in1cpkaf01.innovate.lan:9093,eheu2in1cpkaf01.innovate.lan:9093,eheu2in1cpkaf02.innovate.lan:9093,eheu2in1cpkaf03.innovate.lan:9093,eheu2in1cpkaf04.innovate.lan:9093,eheu2in1cpkaf05.innovate.lan:9093,eheu2in1cpkaf06.innovate.lan:9093";
//config.SaslPassword = "";

//PROD
//config.SaslPassword = "";
//config.BootstrapServers = "ehcuspd1cpkaf01.innovate.lan:9093,ehcuspd1cpkaf01.innovate.lan:9093,ehcuspd1cpkaf02.innovate.lan:9093,ehcuspd1cpkaf03.innovate.lan:9093,ehcuspd1cpkaf04.innovate.lan:9093,ehcuspd1cpkaf05.innovate.lan:9093,ehcuspd1cpkaf06.innovate.lan:9093,ehcuspd1cpkaf07.innovate.lan:9093,ehcuspd1cpkaf08.innovate.lan:9093,ehcuspd1cpkaf09.innovate.lan:9093,ehcuspd1cpkaf10.innovate.lan:9093";
config.NumStreamThreads = 12;
config.SslCaLocation = "./certs/CARoot.crt";

var textFilePath = $"Historical-{DateTime.Now.Ticks}.txt";
var fs = new FileStream(textFilePath, FileMode.Create);
var sw = new StreamWriter(fs);

config.DeserializationExceptionHandler = (context, record, exception) =>
{
    Console.WriteLine($"Encountered {exception.GetType()} during deserialization at topic: {record.Topic}, offset: {record.Offset}. Will continue processing records.");
    Console.WriteLine(exception.Message);
    sw.WriteLine($"Deserialization Error topic: {record.Topic}, offset: {record.Offset} partition: {record.Partition}.");
    return ExceptionHandlerResponse.FAIL;
};

config.InnerExceptionHandler = (exception) =>
{
    Console.WriteLine($"Processing exception: {exception.GetType()}. Will continue processing records.");
    Console.WriteLine(exception.Message);
    if (exception is KStreamException)
    {
        var custom = (KStreamException)exception;
        sw.WriteLine($"Processing exception: {exception.GetType()}. key : {custom.Key}  payload : {custom.Payload} Will continue processing records.");
    }
    sw.WriteLine($"Processing exception: {exception.GetType()}. : Will continue processing records.");
    return ExceptionHandlerResponse.FAIL;
};

config.ProductionExceptionHandler = (deliveryReport) =>
{
    //use to capture exception when producing to topic
    sw.WriteLine($"Processing exception: key: {deliveryReport.Key} offset: {deliveryReport.Offset} partition: {deliveryReport.Partition}. Will continue processing records.");
    return ExceptionHandlerResponse.FAIL;
};

var settings = new JsonSerializerSettings
{
    Converters = { new StringEnumConverter() }
};

StreamBuilder builder = new StreamBuilder();

int countTotal = 0;
int countEP = 0;

builder.Stream<string, string>(readTopic)
        .Filter((k, v) =>
        {
            try
            {
                Console.WriteLine($"total:{countTotal} ep:{countEP}");
                countTotal++;
                UcxExternalSystemStatusChange? message = JsonConvert.DeserializeObject<UcxExternalSystemStatusChange>(v);
                if (message.OriginSystem.ToLower() == "ep" && message.Version == "1.0.0" && message.Sent <= new DateTime(2024, 5, 11, 01, 00, 00, DateTimeKind.Utc))
                {
                    countEP++;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Could not convert string to DateTime"))
                {
                    sw.WriteLine($"Processing exception: {ex.GetType()} mesage: {ex.Message}. key : {k}  payload : {v} : Will continue processing records.");
                    sw.Flush();
                    return false;
                }
                throw new KStreamException(v, k, ex.Message);

            }

        })
          .Map((k, v) =>
          {
              try
              {
                  UcxExternalSystemStatusChange? message = JsonConvert.DeserializeObject<UcxExternalSystemStatusChange>(v);
                  message.Key = Guid.NewGuid();
                  message.Version = "1.0.2";
                  var newMessage = JsonConvert.SerializeObject(message, settings);
                  return KeyValuePair.Create(k, newMessage);
              }
              catch (Exception ex)
              {
                  throw new KStreamException(v, k, ex.Message);
              }
          });
          //.Pr
          //.To<StringSerDes, StringSerDes>(writeTopic);

var stream = new KafkaStream(builder.Build(), config);

await stream.StartAsync(cancellationTokenSource.Token);

Console.CancelKeyPress += (o, e) =>
{
    cancellationTokenSource.Cancel();
    sw.Dispose();
    fs.Close();
    stream.Dispose();
    Environment.Exit(1);
};


Thanks and Regards
Siraj

