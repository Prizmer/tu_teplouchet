using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.IO;
using System.IO.Ports;
using ExcelLibrary.SpreadSheet;
using System.Configuration;
using System.Threading;
//using System.Configuration.Assemblies;

namespace teplouchetapp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        teplouchet1 Meter = null;
        VirtualPort Vp = null;

        //изначально ни один процесс не выполняется, все остановлены
        volatile bool bStopProcess = true;
        bool bPollOnlyOffline = false;

        //default settings for input *.xls file
        int flatNumberColumnIndex = 0;
        int factoryNumberColumnIndex = 1;
        int firstRowIndex = 1;

        private bool initMeterDriver(uint mAddr, string mPass, VirtualPort virtPort)
        {
            if (virtPort == null) return false;

            try
            {
                Meter = new teplouchet1();
                Meter.Init(mAddr, mPass, virtPort);
                return true;
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка инициализации драйвера: " + ex.Message);
                return false;
            }
        }

        private bool refreshSerialPortComboBox()
        {
            try
            {
                string[] portNamesArr = SerialPort.GetPortNames();
                comboBox1.Items.AddRange(portNamesArr);
                if (comboBox1.Items.Count > 0)
                {
                    int startIndex = 0;
                    comboBox1.SelectedIndex = startIndex;
                    return true;
                }
                else
                {
                    WriteToStatus("В системе не найдены доступные COM порты");
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка при обновлении списка доступных COM портов: " + ex.Message);
                return false;
            }
        }

        private bool setVirtualSerialPort()
        {
            try
            {
                SerialPort m_Port = new SerialPort(comboBox1.Items[comboBox1.SelectedIndex].ToString());

                m_Port.BaudRate = int.Parse(ConfigurationSettings.AppSettings["baudrate"]);
                m_Port.DataBits = int.Parse(ConfigurationSettings.AppSettings["databits"]);
                m_Port.Parity = (Parity)int.Parse(ConfigurationSettings.AppSettings["parity"]);
                m_Port.StopBits = (StopBits)int.Parse(ConfigurationSettings.AppSettings["stopbits"]);
                m_Port.DtrEnable = bool.Parse(ConfigurationSettings.AppSettings["dtr"]);
                byte attempts = 1;
                ushort read_timeout = (ushort)numericUpDown1.Value;

                //meters initialized by secondary id (factory n) respond to 0xFD primary addr
                Vp = new ComPort(m_Port, attempts, read_timeout);
                uint mAddr = 0xFD;
                string mPass = "";


                if (!initMeterDriver(mAddr, mPass, Vp)) return false;

                //check vp settings
                SerialPort tmpSP = Vp.getSerialPortObject();
                toolStripStatusLabel2.Text = String.Format("{0} {1}{2}{3} DTR: {4} RTimeout: {5} ms", tmpSP.PortName, tmpSP.BaudRate, tmpSP.Parity, tmpSP.StopBits, tmpSP.DtrEnable, read_timeout); 

                return true;
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка создания виртуального порта: " + ex.Message);
                return false;
            }
        }

        private bool setXlsParser()
        {
            try
            {
                flatNumberColumnIndex = int.Parse(ConfigurationSettings.AppSettings["flatColumn"]) - 1;
                factoryNumberColumnIndex = int.Parse(ConfigurationSettings.AppSettings["factoryColumn"]) - 1;
                firstRowIndex = int.Parse(ConfigurationSettings.AppSettings["firstRow"]) - 1;

                return true;
            }
            catch (Exception ex)
            {
                WriteToStatus("Ошибка разбора блока \"Настройка парсера\" в файле конфигурации: " + ex.Message);
                return false;
            }

        }

        private void WriteToStatus(string str)
        {
            MessageBox.Show(str, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //setting up dialogs
            ofd1.Filter = "Excel files (*.xls) | *.xls";
            sfd1.Filter = ofd1.Filter;
            ofd1.FileName = "FactoryNumbersTable";
            sfd1.FileName = ofd1.FileName;

            if (!refreshSerialPortComboBox()) return;
            if (!setVirtualSerialPort())  return;
            if (!setXlsParser()) return;

            comboBox1.SelectedIndexChanged += new EventHandler(comboBox1_SelectedIndexChanged);
            numericUpDown1.ValueChanged +=new EventHandler(numericUpDown1_ValueChanged);
            meterPinged += new EventHandler(Form1_meterPinged);
            pollingEnd += new EventHandler(Form1_pollingEnd);
            portProblems += new EventHandler(Form1_portProblems);
        }


        DataTable dt = new DataTable("meters");
        public string worksheetName = "Лист1";

        //список, хранящий номера параметров в перечислении Params драйвера
        //целесообразно его сделать сдесь, так как кол-во считываемых значений зависит от кол-ва колонок
        List<int> paramCodes = null;
        private void createMainTable(ref DataTable dt)
        {
            paramCodes = new List<int>();

            //creating columns for internal data table
            DataColumn column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "№ кв.";
            column.ColumnName = "colFlat";

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Счетчик";
            column.ColumnName = "colFactory";

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "На связи  ";
            column.ColumnName = "colOnline";

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Энергия (КВтЧ)";
            column.ColumnName = "colEnergy";
            paramCodes.Add(0);

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Объем (м3)";
            column.ColumnName = "colVolume";
            paramCodes.Add(1);

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Т.входа (С)";
            column.ColumnName = "colTempInp";
            paramCodes.Add(5);

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Т.выхода (С)";
            column.ColumnName = "colTempOutp";
            paramCodes.Add(6);

            column = dt.Columns.Add();
            column.DataType = typeof(string);
            column.Caption = "Вр.работы (Ч)";
            column.ColumnName = "colTimeOn";
            paramCodes.Add(2);

            DataRow captionRow = dt.NewRow();
            for (int i = 0; i < dt.Columns.Count; i++)
                captionRow[i] = dt.Columns[i].Caption;
            dt.Rows.Add(captionRow);

        }

        private void loadXlsFile()
        {
            button6.Enabled = true;

            dt = new DataTable();
            createMainTable(ref dt);
                       
            string fileName = ofd1.FileName;
            Workbook book = Workbook.Load(fileName);
           
            int rowsInFile = 0;
            for (int i = 0; i < book.Worksheets.Count; i++)
                rowsInFile += book.Worksheets[i].Cells.LastRowIndex - firstRowIndex;

            //setting up progress bar
            toolStripProgressBar1.Minimum = 0;
            toolStripProgressBar1.Maximum = rowsInFile;
            toolStripProgressBar1.Step = 1;


            //filling internal data table with *.xls file data according to *.config file
            for (int i = 0; i < book.Worksheets.Count; i++)
            {
                Worksheet sheet = book.Worksheets[i];
                for (int rowIndex = firstRowIndex; rowIndex <= sheet.Cells.LastRowIndex; rowIndex++)
                {
                    if (!bStopProcess)
                    {
                        button6.Enabled = false;
                        return;
                    }

                    Row row_l = sheet.Cells.GetRow(rowIndex);
                    DataRow dataRow = dt.NewRow();

                    object oFlatNumber = row_l.GetCell(flatNumberColumnIndex).Value;
                    int iFlatNumber = 0;

                    if (oFlatNumber != null && int.TryParse(oFlatNumber.ToString(), out iFlatNumber))
                    {
                        dataRow[0] = iFlatNumber;
                        incrProgressBar();
                    }
                    else
                    {
                        incrProgressBar();
                        continue;
                    }

                    dataRow[1] = row_l.GetCell(factoryNumberColumnIndex).Value;

                    dt.Rows.Add(dataRow);
                }
            }


            dgv1.DataSource = dt;

            toolStripProgressBar1.Value = 0;
            toolStripProgressBar1.Maximum = dt.Rows.Count - 1;
            toolStripStatusLabel1.Text = "(0/0)";
            button6.Enabled = false;
            button5.Enabled = true;
            button1.Enabled = true;
            button2.Enabled = true;
            checkBox1.Enabled = true;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (ofd1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                loadXlsFile();
            }
        }

        public event EventHandler meterPinged;
        private void incrProgressBar()
        {
            if (toolStripProgressBar1.Value < toolStripProgressBar1.Maximum)
            {
                toolStripProgressBar1.Value += 1;
                toolStripStatusLabel1.Text = String.Format("({0}/{1})", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
            }
        }

        void Form1_meterPinged(object sender, EventArgs e)
        {
            incrProgressBar();
        }

        public event EventHandler pollingEnd;
        void Form1_pollingEnd(object sender, EventArgs e)
        {
            comboBox1.Enabled = true;
            button1.Enabled = true;
            button2.Enabled = true;
            button3.Enabled = true;
            button5.Enabled = true;
            button6.Enabled = false;
            numericUpDown1.Enabled = true;
            checkBox1.Enabled = true;
        }

        public event EventHandler portProblems;
        void Form1_portProblems(object sender, EventArgs e)
        {
            WriteToStatus("Can't open port");
        }


        Thread pingThr = null;
        private void button2_Click(object sender, EventArgs e)
        {
            toolStripProgressBar1.Value = 0;

            comboBox1.Enabled = false;
            button1.Enabled = false;
            button2.Enabled = false;
            button3.Enabled = false;
            button5.Enabled = false;
            button6.Enabled = true;
            numericUpDown1.Enabled = false;
            checkBox1.Enabled = false;

            bStopProcess = false;

            pingThr = new Thread(pingMeters);
            pingThr.Start((object)dt);
        }


        const string METER_IS_ONLINE = "ОК";
        const string METER_IS_OFFLINE = "Нет связи";
        const string REPEAT_REQUEST = "Повтор";

        int attempts = 4;

        private void pingMeters(Object metersDt)
        {
            DataTable dt = (DataTable)metersDt;
            int columnIndexFactory = 1;
            int columnIndexResult = 2;

            List<string> factoryNumbers = new List<string>();
            for (int i = 1; i < dt.Rows.Count; i++)
            {
                int tmpNumb = 0;
                object oColFactory = dt.Rows[i][columnIndexFactory];
                object oColResult = dt.Rows[i][columnIndexResult];

                //check if already polled
                if (bPollOnlyOffline && (oColResult.ToString() == METER_IS_ONLINE))
                {
                    continue;
                }


                if (oColFactory != null)
                {
                    if (int.TryParse(oColFactory.ToString(), out tmpNumb))
                    {
                        for (int c = 0; c < attempts; c++)
                            if (Meter.SelectBySecondaryId(tmpNumb))
                            {
                                dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                break;
                            }
                            else
                            {
                                if (c == attempts - 1)
                                    dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                else
                                    dt.Rows[i][columnIndexResult] = REPEAT_REQUEST + " " + (c + 1);
                            }
                    }
                }

                Invoke(meterPinged);
                Meter.UnselectAllMeters();

                if (bStopProcess)
                {
                    break;
                }
            }
            

            Invoke(pollingEnd);
        }


        private void pollMeters(Object metersDt)
        {
            DataTable dt = (DataTable)metersDt;
            int columnIndexFactory = 1;
            int columnIndexResult = 2;

            List<string> factoryNumbers = new List<string>();
            for (int i = 1; i < dt.Rows.Count; i++)
            {
                int tmpNumb = 0;
                object o = dt.Rows[i][columnIndexFactory];
                object oColResult = dt.Rows[i][columnIndexResult];

                //check if already polled
                if (bPollOnlyOffline && (oColResult.ToString() == METER_IS_ONLINE))
                {
                    continue;
                }

                if (o != null)
                {
                    if (int.TryParse(o.ToString(), out tmpNumb))
                    {
                        //служит также проверкой связи
                        for (int c = 0; c < attempts; c++)
                            if (Meter.SelectBySecondaryId(tmpNumb))
                            {
                                Thread.Sleep(100);
                                List<float> valList = new List<float>();
                                if (Meter.ReadCurrentValues(paramCodes, out valList))
                                {
                                    for (int j = 0; j < valList.Count; j++)
                                        dt.Rows[i][columnIndexResult + 1 + j] = valList[j];

                                    dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                    break;
                                }
                                else
                                {
                                    if (c < attempts - 1)
                                    {
                                        dt.Rows[i][columnIndexResult] = REPEAT_REQUEST + " " + (c + 1);
                                        continue;
                                    }

                                    //если тест связи пройден, а текущие не прочитаны, то счетчик на связи
                                     dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;

                                }
                            }
                            else
                            {
                                if (c == attempts - 1)
                                    dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                else
                                    dt.Rows[i][columnIndexResult] = REPEAT_REQUEST + " " + (c + 1);
                            }
                    }
                }

                Invoke(meterPinged);
                Meter.UnselectAllMeters();


                //должно снизить нагрузку на порт
                Thread.Sleep(5);

                if (bStopProcess)
                    break;
            }

            Invoke(pollingEnd);
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort();
        }


        private void button5_Click(object sender, EventArgs e)
        {
            if (sfd1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                //create new xls file
                string file = sfd1.FileName;
                Workbook workbook = new Workbook();
                Worksheet worksheet = new Worksheet(worksheetName);

                //office 2010 will not open file if there is less than 100 cells
                for (int i = 0; i < 100; i++)
                    worksheet.Cells[i, 0] = new Cell("");

                //copying data from data table
                for (int rowIndex = 0; rowIndex < dt.Rows.Count; rowIndex++)
                {
                    for (int colIndex = 0; colIndex < dt.Columns.Count; colIndex++)
                    {
                        worksheet.Cells[rowIndex, colIndex] = new Cell(dt.Rows[rowIndex][colIndex].ToString());
                    }
                }

                workbook.Worksheets.Add(worksheet);
                workbook.Save(file);
            }
        }



        private void button1_Click(object sender, EventArgs e)
        {
            toolStripProgressBar1.Value = 0;

            comboBox1.Enabled = false;
            button1.Enabled = false;
            button2.Enabled = false;
            button3.Enabled = false;
            button5.Enabled = false;
            button6.Enabled = true;
            numericUpDown1.Enabled = false;
            checkBox1.Enabled = false;

            bStopProcess = false;

            if (paramCodes.Count == 0)
            {
                MessageBox.Show("Загрузите исходные данные, список paramCodes пуст");
                return;
            }

            pingThr = new Thread(pollMeters);
            pingThr.Start((object)dt);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            bStopProcess = true;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            bPollOnlyOffline = checkBox1.Checked;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (bStopProcess == false)
            {
                MessageBox.Show("Остановите опрос перед закрытием программы","Напоминание");
                e.Cancel = true;
            }

            if (Vp.isOpened())
                Vp.ClosePort();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            richTextBox1.Clear();
            string str_fact_n = textBox1.Text;
            int tmpNumb = -1;
            if (int.TryParse(str_fact_n, out tmpNumb))
            {
                //служит также проверкой связи
                if (Meter.SelectBySecondaryId(tmpNumb))
                {
                    string resStr = "Метод драйвера GetAllValues вернул false";
                    Meter.GetAllValues(out resStr);
                    richTextBox1.Text = resStr;
                }
                else
                {
                    richTextBox1.Text = "Связь с прибором " + tmpNumb.ToString() + " НЕ установлена";
                }
            }
            else
            {
                richTextBox1.Text = "Невозможно преобразовать серийный номер в число";
            }
        }


    }
}
