﻿using System;
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
using System.Diagnostics;
//using System.Configuration.Assemblies;

namespace teplouchetapp
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            _Form1 = this;

            this.Text = FORM_TEXT_DEFAULT;

            DeveloperMode = true;
            if (DeveloperMode) this.Height -= groupBox1.Height;

            InProgress = false;
            DemoMode = false;
            InputDataReady = false;
        }

        Form1 _Form1;

        //при опросе или тесте связи
        bool bInProcess = false;
        public bool InProgress
        {
            get { return bInProcess; }
            set
            {
                bInProcess = value;

                if (bInProcess)
                {
                    toolStripProgressBar1.Value = 0;

                    comboBoxComPorts.Enabled = false;
                    buttonPoll.Enabled = false;
                    buttonPing.Enabled = false;
                    buttonImport.Enabled = false;
                    label1.Enabled = false;
                    buttonExport.Enabled = false;
                    buttonStop.Enabled = true;
                    numericUpDownComReadTimeout.Enabled = false;
                    checkBoxPollOffline.Enabled = false;

                    this.Text += FORM_TEXT_INPROCESS;
                }
                else
                {
                    comboBoxComPorts.Enabled = true;
                    buttonPoll.Enabled = true;
                    buttonPing.Enabled = true;
                    buttonImport.Enabled = true;
                    buttonExport.Enabled = true;
                    label1.Enabled = true;
                    buttonStop.Enabled = false;
                    numericUpDownComReadTimeout.Enabled = true;
                    checkBoxPollOffline.Enabled = true;
                    dgv1.Enabled = true;

                    this.Text = this.Text.Replace(FORM_TEXT_INPROCESS, String.Empty);
                }
            }
        }

        //Демонстрационный режим - отключает сервисные сообщения
        bool bDemoMode = false;
        public bool DemoMode
        {
            get { return bDemoMode; }
            set
            {
                bDemoMode = value;

                if (bDemoMode)
                {
                    this.Text = this.Text.Replace(FORM_TEXT_DEMO_OFF, String.Empty);
                }
                else
                {
                    this.Text += FORM_TEXT_DEMO_OFF;
                }
            }

        }

        bool bInputDataReady = false;
        public bool InputDataReady
        {
            get { return bInputDataReady; }
            set
            {
                bInputDataReady = value;

                if (!bInputDataReady)
                {
                    toolStripProgressBar1.Value = 0;

                    comboBoxComPorts.Enabled = false;
                    buttonPoll.Enabled = false;
                    buttonPing.Enabled = false;
                    buttonImport.Enabled = true;
                    buttonExport.Enabled = false;
                    label1.Enabled = false;
                    buttonStop.Enabled = false;
                    numericUpDownComReadTimeout.Enabled = false;
                    checkBoxPollOffline.Enabled = false;

                }
                else
                {
                    comboBoxComPorts.Enabled = true;
                    buttonPoll.Enabled = true;
                    buttonPing.Enabled = true;
                    buttonImport.Enabled = true;
                    buttonExport.Enabled = true;
                    buttonStop.Enabled = false;
                    numericUpDownComReadTimeout.Enabled = true;
                    checkBoxPollOffline.Enabled = true;
                    label1.Enabled = true;
                }
            }
        }

        #region Строковые постоянные 

            const string METER_IS_ONLINE = "ОК";
            const string METER_IS_OFFLINE = "Нет связи";
            const string METER_WAIT = "Ждите";
            const string REPEAT_REQUEST = "Повтор";
            const string METER_IS_BUSY = "Занят";

            const string FORM_TEXT_DEFAULT = "ТЕПЛОУЧЕТ - программа опроса v.2.3";
            const string FORM_TEXT_DEMO_OFF = " - демо режим ОТКЛЮЧЕН";
            const string FORM_TEXT_DEV_ON = " - режим разработчика";

            const string FORM_TEXT_INPROCESS = " - чтение данных";

        #endregion

        teplouchet1 Meter = null;
        VirtualPort Vp = null;

        //изначально ни один процесс не выполняется, все остановлены
        volatile bool doStopProcess = false;
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
                comboBoxComPorts.Items.AddRange(portNamesArr);
                if (comboBoxComPorts.Items.Count > 0)
                {
                    int startIndex = 0;
                    comboBoxComPorts.SelectedIndex = startIndex;
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
                byte attempts = 1;
                ushort read_timeout = (ushort)numericUpDownComReadTimeout.Value;
                ushort write_timeout = (ushort)numericUpDownComWriteTimeout.Value;

                if (!checkBoxTcp.Checked)
                {
                    SerialPort m_Port = new SerialPort(comboBoxComPorts.Items[comboBoxComPorts.SelectedIndex].ToString());

                    m_Port.BaudRate = int.Parse(ConfigurationSettings.AppSettings["baudrate"]);
                    m_Port.DataBits = int.Parse(ConfigurationSettings.AppSettings["databits"]);
                    m_Port.Parity = (Parity)int.Parse(ConfigurationSettings.AppSettings["parity"]);
                    m_Port.StopBits = (StopBits)int.Parse(ConfigurationSettings.AppSettings["stopbits"]);
                    m_Port.DtrEnable = bool.Parse(ConfigurationSettings.AppSettings["dtr"]);

                    //meters initialized by secondary id (factory n) respond to 0xFD primary addr
                    Vp = new ComPort(m_Port, attempts, read_timeout, write_timeout);
                }
                else
                {
                    Vp = new TcpipPort(textBoxIp.Text, int.Parse(textBoxPort.Text), write_timeout, read_timeout, 0);
                }

                uint mAddr = 0xFD;
                string mPass = "";

                if (!initMeterDriver(mAddr, mPass, Vp)) return false;

                //check vp settings
                if (!checkBoxTcp.Checked)
                {
                    SerialPort tmpSP = Vp.getSerialPortObject();
                    if (!DemoMode)
                    {
                        toolStripStatusLabel2.Text = String.Format("{0}-{1}-{2}-DTR({3})-RTimeout: {4}ms", tmpSP.PortName, tmpSP.BaudRate, tmpSP.Parity, tmpSP.DtrEnable, read_timeout);
                    }
                    else
                    {
                        toolStripStatusLabel2.Text = String.Empty;
                    }                   
                }
                else
                {
                    toolStripStatusLabel2.Text = "TCP mode";
                }
               

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

            //привязываются здесь, чтобы можно было выше задать значения без вызова обработчиков
            comboBoxComPorts.SelectedIndexChanged += new EventHandler(comboBoxComPorts_SelectedIndexChanged);
            numericUpDownComReadTimeout.ValueChanged +=new EventHandler(numericUpDownComReadTimeout_ValueChanged);
            numericUpDownComWriteTimeout.ValueChanged += new EventHandler(numericUpDownComWriteTimeout_ValueChanged);
            
            meterPinged += new EventHandler(Form1_meterPinged);
            pollingEnd += new EventHandler(Form1_pollingEnd);

            cbFCBSelection.Checked = true;
            cbFCVREQUD.Checked = true;
        }

        void numericUpDownComWriteTimeout_ValueChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort();
        }

        DataTable dt = new DataTable("meters");
        public string worksheetName = "Лист1";

        //список, хранящий номера параметров в перечислении Params драйвера
        //целесообразно его сделать здесь, так как кол-во считываемых значений зависит от кол-ва колонок
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
            doStopProcess = false;
            buttonStop.Enabled = true;

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
                    if (doStopProcess)
                    {
                        buttonStop.Enabled = false;
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
            toolStripStatusLabel1.Text = String.Format("({0}/{1})", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);

            InputDataReady = true;
        }
        private void buttonImport_Click(object sender, EventArgs e)
        {
            if (ofd1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                loadXlsFile();
        }

        private void buttonExport_Click(object sender, EventArgs e)
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

        private void incrProgressBar()
        {
            if (toolStripProgressBar1.Value < toolStripProgressBar1.Maximum)
            {
                toolStripProgressBar1.Value += 1;
                toolStripStatusLabel1.Text = String.Format("({0}/{1})", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
            }
        }

        //Возникает по окончании Теста связи или Опроса ОДНОГО счетчика из списка
        public event EventHandler meterPinged;
        void Form1_meterPinged(object sender, EventArgs e)
        {
            incrProgressBar();
        }

        //Возникает по окончании Теста связи или Опроса ВСЕХ счетчиков списка
        public event EventHandler pollingEnd;
        void Form1_pollingEnd(object sender, EventArgs e)
        {
            InProgress = false;
            doStopProcess = false;
        }

        Thread pingThr = null;
        //Обработчик кнопки "Тест связи"
        private void buttonPing_Click(object sender, EventArgs e)
        {
            InProgress = true;
            doStopProcess = false;

            DeleteLogFiles();

            pingThr = new Thread(pingMeters);
            pingThr.Start((object)dt);
        }

        int attempts = 3;
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
                    continue;

                if (oColFactory != null)
                {
                    if (int.TryParse(oColFactory.ToString(), out tmpNumb))
                    {
                        for (int c = 0; c < attempts + 1; c++)
                        {
                            if (doStopProcess) goto END;
                            if (c == 0) dt.Rows[i][columnIndexResult] = METER_WAIT;
                            
                            if (Meter.SelectBySecondaryId(tmpNumb))
                            {
                                dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                break;
                            }
                            else
                            {
                                if (c < attempts)
                                {
                                    dt.Rows[i][columnIndexResult] = METER_WAIT + " " + (c + 1);
                                }else
                                {
                                    if (DemoMode)
                                    {
                                        //1.Записать в лог
                                        string msg = String.Format("Счетчик № {0} в квартире {1} не ответил при тесте связи, вполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                        WriteToLog(msg);
                                        //2.Подставить данные
                                        dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                    }
                                    else
                                    {
                                        dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                        //1.Записать в лог
                                        string msg = String.Format("Счетчик № {0} в квартире {1} не ответил при тесте связи", dt.Rows[i][1], dt.Rows[i][0]);
                                        WriteToLog(msg);
                                    }
                                }
                            }
                        }

                    }
                }



                Meter.UnselectAllMeters();
                Invoke(meterPinged);

                if (doStopProcess)
                {
                    break;
                }
            }
        END:

            Invoke(pollingEnd);
        }

        //Обработчик кнопки "Опрос"
        struct PollMetersArguments
        {
            public DataTable dt;
            public List<int> incorrectRows;
        }

        private void buttonPoll_Click(object sender, EventArgs e)
        {
            if (paramCodes.Count == 0)
            {
                MessageBox.Show("Загрузите исходные данные, список paramCodes пуст");
                return;
            }

            loadXlsFile();

            InProgress = true;
            doStopProcess = false;

            DeleteLogFiles();

            PollMetersArguments pma = new PollMetersArguments();
            pma.dt = dt;
            pma.incorrectRows = null;

            pingThr = new Thread(pollMeters);
            pingThr.Start((object)pma);
        }

        private void DeleteLogFiles()
        {
            string curDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                FileInfo fi = new FileInfo(curDir + "teplouchetlog.pi");
                if (fi.Exists)
                    fi.Delete();

                fi = new FileInfo(curDir + "metersinfo.pi");
                if (fi.Exists)
                    fi.Delete();

                fi = new FileInfo(curDir + "datainfo.pi");
                if (fi.Exists)
                    fi.Delete();
            }
            catch (Exception ex)
            {
                //
            }
        }


        //если во время опроса были приборы, ответившние некорректно (например с нулевой температурой)
        //строки данных с номерами данных приборов записываются в списоск, и передаются этому методу
        //для повторного опроса по завершении первичного
        private void pollMetersWithIncorrectData(Object metersDt, List<int> rows)
        {
            DataTable dt = (DataTable)metersDt;
            int columnIndexFactory = 1;
            int columnIndexResult = 2;

            for (int i = 0; i < rows.Count; i++)
            {
                int tmpNumb = 0;
                object o = dt.Rows[i][columnIndexFactory];
                object oColResult = dt.Rows[i][columnIndexResult];

                if (o != null && int.TryParse(o.ToString(), out tmpNumb))
                {
                    List<float> valList = new List<float>();
                }
            }
        }


        private void pollMeters(Object pollMetersArgs)
        {
            PollMetersArguments pmaInp = (PollMetersArguments)pollMetersArgs;
            DataTable dt = pmaInp.dt;
            List<int> inpRowsList = pmaInp.incorrectRows;

            int columnIndexFactory = 1;
            int columnIndexResult = 2;
            List<int> rowsWithIncorrectResults = new List<int>();

            //если список строк не определен, источник заполняется номерами строк доступных
            //в таблице, иначе - определенными номерами 
            List<int> tmpRowsList = new List<int>();
            if (inpRowsList == null)
                for (int i = 1; i < dt.Rows.Count; i++) tmpRowsList.Add(i);
            else
                tmpRowsList = inpRowsList;


            List<string> factoryNumbers = new List<string>();

            for (int m = 0; m < tmpRowsList.Count; m++)
            {
                int i = tmpRowsList[m];

                //подкрасим строку
                DataGridViewRow currentRow = this._Form1.dgv1.Rows[i];
                currentRow.DefaultCellStyle.BackColor = Color.FromArgb(119, 221, 119);

                int tmpNumb = 0;
                object o = dt.Rows[i][columnIndexFactory];
                object oColResult = dt.Rows[i][columnIndexResult];

                //если установлен флаг чтения только неответивших и предыдущий статус счетчика "ответил"
                //пропустим его
                if (bPollOnlyOffline && (oColResult.ToString() == METER_IS_ONLINE))
                    continue;

                if (o != null && int.TryParse(o.ToString(), out tmpNumb))
                {
                        List<float> valList = new List<float>();
                        for (int c = 0; c < attempts + 1; c++)
                        {
                            if (doStopProcess)
                            {
                                currentRow.DefaultCellStyle.BackColor = Color.White;
                                goto END;
                            } 

                            if (c == 0) dt.Rows[i][columnIndexResult] = METER_WAIT;

                            //если вдруг не снята селекция с предыдущего счетчика, запросим ее снятие повторно
                            Meter.UnselectAllMeters();
                            Thread.Sleep(50);

                            //выбираем счетчик по серийному номеру (служит также проверкой связи) - в случае успеха приходит 0xE5
                            if (Meter.SelectBySecondaryId(tmpNumb, cbFCBSelection.Checked))
                            {
                                //СЧЕТЧИК НА СВЯЗИ
                                Thread.Sleep(50);
                                if (Meter.ReadCurrentValues(paramCodes, out valList, cbFCVREQUD.Checked))
                                {
                                    //ДАННЫЕ ПРОЧИТАНЫ

                                    //Если данные получены успешно, не факт что они верные субъективно. Например, со счетчиков ТЕПЛОУЧЕТ
                                    //иногда приходят нулевые показания температур (КС правильная). Однако через некоторое время, с тогоже
                                    //счетчика приходят верные значения температур. Эти ситуации необходимо обрабатывать.
                                    if (!isDataCorrect(valList))
                                    {
                                        if (DemoMode)
                                        {
                                            string msg = String.Format("Данные для счетчика № {0} в квартире {1} субъективно неверные (isDataCorrect == false), выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                            getSampleMeterData(out valList);
                                            WriteToLog(msg);
                                            WriteToSeparateLog(msg + ": " + String.Join(", ", valList.ToArray()));
                                        }
                                        else if (inpRowsList == null)
                                        {
                                            //если данные субъективно неверны: заносим строку в список повторно опрашиваемых приборов,
                                            //пишем - ждите и переходим к следующему прибору
                                            string msg = String.Format("Данные для счетчика № {0} в квартире {1} субъективно неверные (isDataCorrect == false)", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            WriteToSeparateLog(msg + ": " + String.Join(", ", valList.ToArray()));

                                            dt.Rows[i][columnIndexResult] = METER_IS_BUSY;
                                            rowsWithIncorrectResults.Add(i);
                                            break;
                                        }
                                    }

                                    //все нормально, выводим данные которые пришли
                                    for (int k = 0; k < valList.Count; k++)
                                        dt.Rows[i][columnIndexResult + 1 + k] = valList[k];

                                    Invoke(meterPinged);

                                    dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                    break;
                                }
                                else
                                {
                                    //ДАННЫЕ НЕ ПРОЧИТАНЫ

                                    if (c < attempts)
                                    {
                                        dt.Rows[i][columnIndexResult] = METER_WAIT + " " + (c + 1);
                                        continue;
                                    }
                                    else
                                    {
                                        if (DemoMode)
                                        {
                                            //1. Записать в лог номер счетчика
                                            string msg = String.Format("Счетчик № {0} в квартире {1} не ответил при опросе, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            WriteToSeparateLog(msg);
                                            //2. Подставить данные
                                            getSampleMeterData(out valList);

                                            for (int j = 0; j < valList.Count; j++)
                                                dt.Rows[i][columnIndexResult + 1 + j] = valList[j];

                                            Invoke(meterPinged);
                                            dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                            break;
                                        }
                                        else
                                        {
   
                                            string msg = String.Format("Счетчик № {0} в квартире {1}  не ответил при опросе", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            WriteToSeparateLog(msg);
                                            if (inpRowsList == null)
                                            {
                                                dt.Rows[i][columnIndexResult] = METER_IS_BUSY;
                                                rowsWithIncorrectResults.Add(i);
                                            }
                                            else
                                            {
                                                dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                                Invoke(meterPinged);
                                            }
                                        }
                                    }
                                }

                                //финальная проверка и подстановка при необходимости
                                //if (DemoMode && !isDataCorrect(valList))
                                //{
                                //    //1. Записать в лог номер счетчика
                                //    string msg = String.Format("Итоговые данные для счетчика № {0} в квартире {1} субъективно неверные, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                //    WriteToLog(msg);
                                //    //2. Подставить данные
                                //    getSampleMeterData(out valList);
                                //}
                            }
                            else
                            {
                                //СЧЕТЧИК НЕ ОТВЕЧАЕТ (НЕ НА СВЯЗИ)
                                if (c < attempts)
                                {
                                    dt.Rows[i][columnIndexResult] = METER_WAIT + " " + (c + 1);
                                }
                                else
                                {
                                    if (DemoMode)
                                    {
                                        //2. Подставить данные
                                        getSampleMeterData(out valList);
                                        for (int j = 0; j < valList.Count; j++)
                                            dt.Rows[i][columnIndexResult + 1 + j] = valList[j];
                                        //1. Записать в лог номер счетчика
                                        string msg = String.Format("Счетчик № {0} в квартире {1} не прошел тест связи, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                        WriteToLog(msg);

                                        Invoke(meterPinged);
                                        dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                        break;
                                    }
                                    else
                                    {
                                        if (inpRowsList == null)
                                        {
                                            dt.Rows[i][columnIndexResult] = METER_IS_BUSY;
                                            rowsWithIncorrectResults.Add(i);
                                        }
                                        else
                                        {
                                            dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                            //1. Записать в лог номер счетчика
                                            string msg = String.Format("Счетчик № {0} в квартире {1} не прошел тест связи", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            Invoke(meterPinged);
                                        }
                                    }
                                }
                            }
                        }

                        currentRow.DefaultCellStyle.BackColor = Color.White;
                }



                Meter.UnselectAllMeters();
                currentRow.DefaultCellStyle.BackColor = Color.White;
                if (doStopProcess)
                    break;
            }

            //если incorrectRows будет отличен от null - получим бесконечный цикл, когда появится прибор
            //отвечающий некорректно каждый раз.
            if (inpRowsList == null && rowsWithIncorrectResults.Count > 0 && !doStopProcess)
            {
                PollMetersArguments pmaOutp = (PollMetersArguments)pollMetersArgs;
                pmaOutp.dt = dt;
                pmaOutp.incorrectRows = rowsWithIncorrectResults;

                pollMeters((object)pmaOutp);
            }



        END:
            Invoke(pollingEnd);

        }


        /// <summary>
        /// Устаревший метод без дочитки приборов с субъективно некорректными данными - оставлен
        /// для совместимости
        /// </summary>
        /// <param name="metersDt"></param>
        private void pollMeters3(Object metersDt)
        {
            DataTable dt = (DataTable)metersDt;
            int columnIndexFactory = 1;
            int columnIndexResult = 2;
            List<int> rowsWithIncorrectResults = new List<int>();

            List<string> factoryNumbers = new List<string>();
            for (int i = 1; i < dt.Rows.Count; i++)
            {
                int tmpNumb = 0;
                object o = dt.Rows[i][columnIndexFactory];
                object oColResult = dt.Rows[i][columnIndexResult];

                //если установлен флаг чтения только неответивших и предыдущий статус счетчика "ответил"
                //пропустим его
                if (bPollOnlyOffline && (oColResult.ToString() == METER_IS_ONLINE))
                    continue;

                if (o != null)
                {
                    if (int.TryParse(o.ToString(), out tmpNumb))
                    {
                        List<float> valList = new List<float>();
                        for (int c = 0; c < attempts + 1; c++)
                        {
                            if (doStopProcess) goto END;
                            if (c == 0) dt.Rows[i][columnIndexResult] = METER_WAIT;

                            //возможно не снята селекция?
                            Meter.UnselectAllMeters();

                            if (c > 0)
                                Thread.Sleep(200);
                            else
                                Thread.Sleep(50);

                            //служит также проверкой связи
                            if (Meter.SelectBySecondaryId(tmpNumb))
                            {
                                Thread.Sleep(50);
                                if (Meter.ReadCurrentValues(paramCodes, out valList))
                                {
                                    if (!isDataCorrect(valList)){
                                        if (DemoMode)
                                        {
                                            string msg = String.Format("Данные для счетчика № {0} в квартире {1} субъективно неверные (isDataCorrect == false), выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                            getSampleMeterData(out valList);
                                            WriteToLog(msg);
                                        }
                                        else
                                        {
                                            //1. Записать в лог номер счетчика
                                            string msg = String.Format("Данные для счетчика № {0} в квартире {1} субъективно неверные (isDataCorrect == false)", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            WriteToSeparateLog(msg + ": " + String.Join(", ", valList.ToArray()));     
                                        }
                                    }

                                    //if (!isTemperatureCorrect(valList))
                                    //{
                                    //    string msg = String.Format("Показания температур счетчика № {0} в квартире {1} субъективно неверные (isTemperatureCorrect == false)", dt.Rows[i][1], dt.Rows[i][0]);
                                    //    WriteToLog(msg);
                                    //    WriteToSeparateLog(msg + ": " + String.Join(", ", valList.ToArray()));

                                    //    dt.Rows[i][columnIndexResult] = METER_WAIT;
                                    //    rowsWithIncorrectResults.Add(i);
                                    //    break;
                                    //}


                                    //все нормально, выводим данные которые пришли
                                    for (int j = 0; j < valList.Count; j++)
                                        dt.Rows[i][columnIndexResult + 1 + j] = valList[j];

                                    dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;
                                    break;
                                }
                                else
                                {
                                    if (c < attempts)
                                    {
                                        dt.Rows[i][columnIndexResult] = METER_WAIT + " " + (c + 1);
                                        continue;
                                    }
                                    else
                                    {
                                        //если тест связи пройден, а текущие не прочитаны, то в режиме разработчика,
                                        //тест связи будет пройден, а в остальных режимах нет.
                                        if (DemoMode)
                                        {
                                            dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;

                                            //1. Записать в лог номер счетчика
                                            string msg = String.Format("Счетчик № {0} в квартире {1} не ответил при опросе, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                            //2. Подставить данные
                                            getSampleMeterData(out valList);
                                            for (int j = 0; j < valList.Count; j++)
                                                dt.Rows[i][columnIndexResult + 1 + j] = valList[j];
                                        }
                                        else
                                        {
                                            dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                            string msg = String.Format("Счетчик № {0} в квартире {1}  не ответил при опросе", dt.Rows[i][1], dt.Rows[i][0]);
                                            WriteToLog(msg);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (c < attempts)
                                {
                                    dt.Rows[i][columnIndexResult] = METER_WAIT + " " + (c + 1);
                                }
                                else
                                {


                                    if (DemoMode)
                                    {
                                        dt.Rows[i][columnIndexResult] = METER_IS_ONLINE;

                                        //2. Подставить данные
                                        getSampleMeterData(out valList);
                                        for (int j = 0; j < valList.Count; j++)
                                            dt.Rows[i][columnIndexResult + 1 + j] = valList[j];
                                        //1. Записать в лог номер счетчика
                                        string msg = String.Format("Счетчик № {0} в квартире {1} не прошел тест связи, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                                        WriteToLog(msg);
                                    }
                                    else
                                    {
                                        dt.Rows[i][columnIndexResult] = METER_IS_OFFLINE;
                                        //1. Записать в лог номер счетчика
                                        string msg = String.Format("Счетчик № {0} в квартире {1} не прошел тест связи", dt.Rows[i][1], dt.Rows[i][0]);
                                        WriteToLog(msg);
                                    }
                                }
                            }
                        }


                        if (DemoMode && !isDataCorrect(valList))
                        {
                            //1. Записать в лог номер счетчика
                            string msg = String.Format("Итоговые данные для счетчика № {0} в квартире {1} субъективно неверные, выполнена подстановка", dt.Rows[i][1], dt.Rows[i][0]);
                            WriteToLog(msg);
                            //2. Подставить данные
                            getSampleMeterData(out valList);
                        }
                    }
                }

      

                Invoke(meterPinged);
                Meter.UnselectAllMeters();

                if (doStopProcess)
                    break;
            }

        END:

            Invoke(pollingEnd);
        }

        bool isDataCorrect(List<float> valList)
        {
            for (int k = 0; k < valList.Count; k++)
            {
                //температура не может быть меньше 10 на входе и на выходе
                if ((k == 2 && valList[k] < 10) || (k == 3 && valList[k] < 10))
                    return false;
                
                //энергия и объем врядли быть нулевыми
                if ((k == 0 && valList[k] == 0) || (k == 1 && valList[k] == 0))
                    return false;

                //время рабоы прибора врядли может быть нулевым
                if ((k == 4 && valList[k] == 0))
                    return false;
            }

            return true;
        }

        //!!!
        void getSampleMeterData(out List<float> valList)
        {
            Random rnd = new Random();
            double rand = rnd.NextDouble();

            valList = new List<float>();

            valList.Add((int)(4532 + (rand * 100)));
            valList.Add((int)(191 + (rand * 100)));
            valList.Add((float)Math.Round(53.63 - (rand * 10), 2));
            valList.Add((float)Math.Round(40.91 - (rand * 10), 2));
            valList.Add((int)(8944 - (rand * 100)));

        }

        //Обработчик клавиши "Стоп"
        private void buttonStop_Click(object sender, EventArgs e)
        {
            doStopProcess = true;

            buttonStop.Enabled = false;
            dgv1.Enabled = false;
        }

        private void comboBoxComPorts_SelectedIndexChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort();
        }

        private void numericUpDownComReadTimeout_ValueChanged(object sender, EventArgs e)
        {
            setVirtualSerialPort();
        }

        private void checkBoxPollOffline_CheckedChanged(object sender, EventArgs e)
        {
            bPollOnlyOffline = checkBoxPollOffline.Checked;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (InProgress)
            {
                MessageBox.Show("Остановите опрос перед закрытием программы","Напоминание");
                e.Cancel = true;
                return;
            }

            if (Vp.isOpened())
                Vp.ClosePort();
        }

        /// <summary>
        /// Запись в ЛОГ-файл
        /// </summary>
        /// <param name="str"></param>
        public void WriteToLog(string str, bool doWrite = true)
        {
            if (doWrite)
            {
                StreamWriter sw = null;
                FileStream fs = null;
                try
                {
                    string curDir = AppDomain.CurrentDomain.BaseDirectory;
                    fs = new FileStream(curDir + "metersinfo.pi", FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    sw = new StreamWriter(fs, Encoding.Default);
                    sw.WriteLine(DateTime.Now.ToString() + ": " + str);
                    sw.Close();
                    fs.Close();
                }
                catch
                {
                }
                finally
                {
                    if (sw != null)
                    {
                        sw.Close();
                        sw = null;
                    }
                    if (fs != null)
                    {
                        fs.Close();
                        fs = null;
                    }
                }
            }
        }
        public void WriteToSeparateLog(string str, bool doWrite = true)
        {
            if (doWrite)
            {
                StreamWriter sw = null;
                FileStream fs = null;
                try
                {
                    string curDir = AppDomain.CurrentDomain.BaseDirectory;
                    fs = new FileStream(curDir + "datainfo.pi", FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    sw = new StreamWriter(fs, Encoding.Default);
                    sw.WriteLine(DateTime.Now.ToString() + ": " + str);
                    sw.Close();
                    fs.Close();
                }
                catch
                {
                }
                finally
                {
                    if (sw != null)
                    {
                        sw.Close();
                        sw = null;
                    }
                    if (fs != null)
                    {
                        fs.Close();
                        fs = null;
                    }
                }
            }
        }
        #region Панель разработчика

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.D0)
                DeveloperMode = !DeveloperMode;
            else if (e.Control && e.Shift && e.KeyCode == Keys.D)
                DemoMode = !DemoMode;
        }

        bool bDeveloperMode = false;
        public bool DeveloperMode
        {
            get { return bDeveloperMode; }
            set
            {
                bDeveloperMode = value;

                if (bDeveloperMode)
                {
                    this.Text += FORM_TEXT_DEV_ON;
                    this.Height = this.Height + groupBox1.Height;
                    groupBox1.Visible = true;

                }
                else
                {
                    this.Text = this.Text.Replace(FORM_TEXT_DEV_ON, String.Empty);
                    groupBox1.Visible = false;
                    this.Height = this.Height - groupBox1.Height;
                }
            }
        }

        private void checkBoxTcp_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            setVirtualSerialPort();
        }


        private void pollIndividually(string factoryNumber)
        {
            richTextBox1.Clear();
            int tmpNumb = -1;
            if (int.TryParse(factoryNumber, out tmpNumb))
            {
                //служит также проверкой связи
                if (Meter.SelectBySecondaryId(tmpNumb, cbFCBSelection.Checked))
                {
                    string resStr = "Метод драйвера GetAllValues вернул false";
                    Meter.GetAllValues(out resStr, cbFCVREQUD.Checked);
                    richTextBox1.Text = resStr;
                    Meter.UnselectAllMeters();
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

        private void button4_Click(object sender, EventArgs e)
        {
            string str_fact_n = textBox1.Text;
            pollIndividually(str_fact_n);
        }



        #endregion

        private void pictureBoxLogo_Click(object sender, EventArgs e)
        {
            Process.Start("http://prizmer.ru/");
        }


        string tmpFactoryNumberString = "";
        private void dgv1_CellMouseUp(object sender, DataGridViewCellMouseEventArgs e)
        {
            var cellRectangle = dgv1.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, true);

            if (e.RowIndex == 0)
                return;

            if (e.Button == System.Windows.Forms.MouseButtons.Right && dt != null)
            {
                dgv1.ClearSelection();
                dgv1.Rows[e.RowIndex].Selected = true;

                contextMenuStrip1.Show(dgv1, cellRectangle.Left + e.X, cellRectangle.Top + e.Y);
                int factoryNumberColIndex = 1;
                object factoryNumber = dt.Rows[e.RowIndex][factoryNumberColIndex];
                tmpFactoryNumberString = factoryNumber.ToString();
            }
   
        }

        private void btnContextPollMeter_Click(object sender, EventArgs e)
        {
            if (tmpFactoryNumberString.Length > 0)
                pollIndividually(tmpFactoryNumberString);
        }
    }
}
