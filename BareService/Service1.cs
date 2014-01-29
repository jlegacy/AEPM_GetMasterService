using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceModel;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using BareService.GetMasterWebService;
using BareService.Properties;
using IBM.Data.DB2.iSeries;

namespace BareService
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple)]
    public partial class Service1 : ServiceBase
    {
        // This is a flag to indicate the service status
        private bool _serviceStarted;

        // the thread that will do the work
        private Thread _workerThread;

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            WriteToEventLog("AEPM Service Starting...");

            // Create worker thread; this will invoke the WorkerFunction
            // when we start it.
            // Since we use a separate worker thread, the main service
            // thread will return quickly, telling Windows that service has started
            var st = new ThreadStart(WorkerFunction);
            _workerThread = new Thread(st);

            // set flag to indicate worker thread is active
            _serviceStarted = true;

            // start the thread
            _workerThread.Start();
        }

        protected override void OnStop()
        {
            // flag to tell the worker process to stop
            _serviceStarted = false;

            // give it a little time to finish any pending work
            _workerThread.Join(new TimeSpan(0, 2, 0));
        }

        private void WorkerFunction()
        {
            // start an endless loop; loop will abort only when "serviceStarted"
            // flag = false
            while (_serviceStarted)
            {
                // do something
                // exception handling omitted here for simplicity

                // yield
                if (_serviceStarted)
                {
                    Thread.Sleep(new TimeSpan(0, 0, 0));
                }

                GetAepmMaster();
            }

            // time to end the thread
            Thread.CurrentThread.Abort();
        }

        public async Task<string> TestAsync(DataTable dt, int i)
        {
            return await TaskEx.Run(() =>
            {
                string guid = GetGuidString(dt, i);
                string part = GetPartString(dt, i);

                //Under Settings, use the following links to either point to production or test
                //production = http://enmiis01.global.nmhg.corp/AEPM_services/Services.svc'
                //test = http://enmdevex.global.nmhg.corp:82/AEPM_services/Services.svc'

                IServices client = new ServicesClient();

                GetMasterResult getResult = client.GetMaster(part);

                Type objectType = getResult.GetType();
                var xmlSerializer = new XmlSerializer(objectType);
                var memoryStream = new MemoryStream();
                using (var xmlTextWriter =
                    new XmlTextWriter(memoryStream, Encoding.Default) {Formatting = Formatting.None})
                {
                    xmlSerializer.Serialize(xmlTextWriter, getResult);
                    memoryStream = (MemoryStream) xmlTextWriter.BaseStream;
// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                    new UTF8Encoding().GetString(memoryStream.ToArray());
                    memoryStream.Dispose();
                    // return xmlText;
                }

                if (getResult.Error == null)
                {
                    using (var conn = new iDB2Connection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
                    {
                        string query = GetUpdateMasterString();

                        var objDataAdapter = new iDB2DataAdapter();

                        var cmd = new iDB2Command(query, conn);

                        cmd.Connection.Open();

                        objDataAdapter.UpdateCommand = cmd;
                        objDataAdapter.UpdateCommand.CommandTimeout = 0;
                        cmd.Parameters.Add("@guid", iDB2DbType.iDB2Char);
                        cmd.Parameters["@guid"].Value = guid;

                        cmd.Parameters.Add("@usrid", iDB2DbType.iDB2Char);
                        cmd.Parameters["@usrid"].Value = (getResult.UserID.Trim().Length > 0) ? getResult.UserID : " ";

                        cmd.Parameters.Add("@branded", iDB2DbType.iDB2Char);
                        cmd.Parameters["@branded"].Value = getResult.Branded;

                        cmd.Parameters.Add("@comcode", iDB2DbType.iDB2Char);
                        cmd.Parameters["@comcode"].Value = (getResult.Commodity_Code.Trim().Length > 0)
                            ? getResult.Commodity_Code
                            : " ";

                        cmd.Parameters.Add("@level", iDB2DbType.iDB2Integer);
                        cmd.Parameters["@level"].Value = getResult.Level;

                        cmd.Parameters.Add("@status", iDB2DbType.iDB2Char);
                        cmd.Parameters["@status"].Value = (getResult.Status.Trim().Length > 0) ? getResult.Status : " ";

                        cmd.Parameters.Add("@rtnble", iDB2DbType.iDB2Char);
                        cmd.Parameters["@rtnble"].Value = getResult.Returnable;

                        cmd.Parameters.Add("@tariffcd", iDB2DbType.iDB2Char);
                        cmd.Parameters["@tariffcd"].Value = (getResult.Tariff_Code.Trim().Length > 0)
                            ? getResult.Tariff_Code
                            : " ";

                        cmd.Parameters.Add("@amsc", iDB2DbType.iDB2Char);
                        cmd.Parameters["@amsc"].Value = (getResult.AMSC.Trim().Length > 0) ? getResult.AMSC : " ";

                        cmd.Parameters.Add("@tqty", iDB2DbType.iDB2Integer);
                        cmd.Parameters["@tqty"].Value = getResult.Technical_Qty;

                        cmd.Parameters.Add("@svclife", iDB2DbType.iDB2Integer);
                        cmd.Parameters["@svclife"].Value = getResult.Service_Life;

                        cmd.Parameters.Add("@pkgcode", iDB2DbType.iDB2Char);
                        cmd.Parameters["@pkgcode"].Value = (getResult.Package_Code.Trim().Length > 0)
                            ? getResult.Package_Code
                            : " ";

                        cmd.Parameters.Add("@info", iDB2DbType.iDB2Char);
                        cmd.Parameters["@info"].Value = (getResult.Information.Trim().Length > 0)
                            ? getResult.Information
                            : " ";

                        cmd.Parameters.Add("@retrn", iDB2DbType.iDB2Char);
                        cmd.Parameters["@retrn"].Value = 'R';

                        cmd.ExecuteNonQuery();
                        cmd.Connection.Close();
                    }

                    foreach (CrossPart s in getResult.CrossPartList)
                    {
                        using (
                            var conn = new iDB2Connection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
                        {
                            string query = GetCrossPartInsertString();

                            var objDataAdapter = new iDB2DataAdapter();

                            var cmd = new iDB2Command(query, conn);

                            cmd.Connection.Open();

                            objDataAdapter.InsertCommand = cmd;
                            objDataAdapter.InsertCommand.CommandTimeout = 0;
                            cmd.Parameters.Add("@guid", iDB2DbType.iDB2Char);
                            cmd.Parameters["@guid"].Value = guid;

                            cmd.Parameters.Add("@item", iDB2DbType.iDB2Char);
                            cmd.Parameters["@item"].Value = s.PartNumber;

                            cmd.Parameters.Add("@brand", iDB2DbType.iDB2Char);
                            cmd.Parameters["@brand"].Value = s.Brand;

                            cmd.ExecuteNonQuery();
                            cmd.Connection.Close();
                        }
                    }
                }
                return i.ToString(CultureInfo.InvariantCulture);
            });
        }

        private static string GetPartString(DataTable dt, int i)
        {
            var sb = new StringBuilder();
            sb.Append(dt.Rows[i]["G_ITEM"]);
            return sb.ToString();
        }

        private static string GetGuidString(DataTable dt, int i)
        {
            var sb = new StringBuilder();
            sb.Append(dt.Rows[i]["G_GUID"]);
            return sb.ToString();
        }


        public void GetAepmMaster()
        {
            var dt = new DataTable();
            var dset = new DataSet();

            //retrieve any records needing updating
            try
            {
                using (var conn = new iDB2Connection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
                {
                    //  string sql = @"SELECT G_GUID, G_ITEM, G_RETRN FROM CGJLEGAC.GETMSTRL1 WHERE G_RETRN != 'R' and G_RETRN != 'S'";
                    string sql = GetUnprocessMasterRecsString();
                    var objDataAdapter = new iDB2DataAdapter();
                    var cmd = new iDB2Command(sql, conn);

                    objDataAdapter.SelectCommand = cmd;
                    objDataAdapter.SelectCommand.CommandTimeout = 0;

                    dt.Clear();
                    dset.Clear();

                    objDataAdapter.Fill(dt);
                    objDataAdapter.Fill(dset, "currentSelections");

                    var cb = new iDB2CommandBuilder(objDataAdapter);
                    AddParameters(cb);

                    //update records to 'S' for submitted
                    for (int i = 0; i < dset.Tables["currentSelections"].Rows.Count; i++)
                    {
                        dset.Tables["currentSelections"].Rows[i]["G_RETRN"] = 'S';
                    }

                    objDataAdapter.Update(dset, "currentSelections");

                    var taskList = new Task[dt.Rows.Count];

                    //submit records asynch
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        taskList[i] = TestAsync(dt, i);
                    }

                    Task.WaitAll(taskList);
                }
            }
            catch (Exception ex)
            {
                WriteToEventLog(ex);
            }
        }

        private static void WriteToEventLog(Exception ex)
        {
            var myLog = new EventLog();
            myLog.Source = "Application Log";
            myLog.WriteEntry("as400 exception:" + ex, EventLogEntryType.Information);
        }

        private static void WriteToEventLog(String x)
        {
            var myLog = new EventLog();
            myLog.Source = "Application Log";
            myLog.WriteEntry(x, EventLogEntryType.Information);
        }

        private static string GetUnprocessMasterRecsString()
        {
            var sb = new StringBuilder();
            sb.Append(@"SELECT G_GUID, G_ITEM, G_RETRN FROM ");
            sb.Append(Settings.Default.partFileL1);
            sb.Append(" WHERE G_RETRN != 'R'");
            return sb.ToString();
        }

        private static string GetCrossPartInsertString()
        {
            var sb = new StringBuilder();
            sb.Append("INSERT into ");
            sb.Append(Settings.Default.partXRefFile);
            sb.Append(" (X_GUID,X_ITEM,X_BRAND) VALUES(@guid, @item, @brand)");
            return sb.ToString();
        }

        private static string GetUpdateMasterString()
        {
            var sb = new StringBuilder();
            sb.Append("UPDATE ");
            sb.Append(Settings.Default.partFile);
            sb.Append(
                " SET G_USRID = @usrid,G_BRANDED = @branded,G_COMCODE = @comcode,G_LEVEL = @level, G_STATUS = @status, G_RTNBLE = @rtnble, G_TARIFFCD = @tariffcd, G_AMSC = @amsc, G_TQTY = @tqty, G_SVCLIFE = @svclife, G_PKGCODE = @pkgcode, G_INFO = @info, G_RETRN = @retrn WHERE G_GUID = @guid");
            return sb.ToString();
        }

        // Define the parameters for the UPDATE command in different ways
        public static void AddParameters(iDB2CommandBuilder cb)
        {
            try
            {
                cb.GetUpdateCommand().Parameters.Add("@return", iDB2DbType.iDB2Char, 1, "G_RETRN");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}