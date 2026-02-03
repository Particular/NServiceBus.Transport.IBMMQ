using IBM.WMQ;
using IBM.WMQ.PCF;

try
{
    // Set MQ connection properties
    MQEnvironment.Hostname = "localhost";
    MQEnvironment.Channel = "DEV.ADMIN.SVRCONN";
    MQEnvironment.Port = 1414;
    MQEnvironment.UserId = "admin";
    MQEnvironment.Password = "passw0rd";
    // Connect to the queue manager
    using MQQueueManager queueManager = new MQQueueManager("QM1");
    Console.WriteLine("✅ Connected to queue manager QM1");

    var agent = new PCFMessageAgent(queueManager);

    // --------------------------------------------------------
    // 1) CREATE A LOCAL QUEUE
    // --------------------------------------------------------
    var createQueueRequest = new PCFMessage(MQC.MQCMD_CREATE_Q);
    createQueueRequest.AddParameter(MQC.MQCA_Q_NAME, "NEW.QUEUE");
    createQueueRequest.AddParameter(MQC.MQIA_Q_TYPE, MQC.MQQT_LOCAL);

    var queueResponse = agent.Send(createQueueRequest);
    if (queueResponse != null)
    {
        Console.WriteLine("✅ Queue 'NEW.QUEUE' creation request sent");
    }
    else
    {
        Console.WriteLine("❌ Queue 'NEW.QUEUE' creation request failed");
    }

    // --------------------------------------------------------
    // 2) CREATE A TOPIC
    // --------------------------------------------------------
    var createTopicRequest = new PCFMessage(MQC.MQCMD_CREATE_TOPIC);
    // The "name" of the topic object as it appears in the MQ Explorer
    createTopicRequest.AddParameter(MQC.MQCA_TOPIC_NAME, "NEW.TOPIC");
    // The actual topic string used by publishers/subscribers
    createTopicRequest.AddParameter(MQC.MQCA_TOPIC_STRING, "dev/test/topic");

    var topicResponse = agent.Send(createTopicRequest);
    if (topicResponse != null)
    {
        Console.WriteLine("✅ Topic 'NEW.TOPIC' creation request sent");
    }
    else
    {
        Console.WriteLine("❌ Topic 'NEW.TOPIC' creation request failed");
    }
}
catch (MQException ex)
{
    Console.WriteLine($"❌ MQException: {ex.ReasonCode} - {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Exception: {ex.Message}");
}
