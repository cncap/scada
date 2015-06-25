﻿/*
 * Copyright 2015 Mikhail Shiryaev
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * 
 * Product  : Rapid SCADA
 * Module   : ModDBExport
 * Summary  : Server module logic
 * 
 * Author   : Mikhail Shiryaev
 * Created  : 2015
 * Modified : 2015
 * 
 * Description
 * Server module for real time data export from Rapid SCADA to DB.
 */

using Scada.Data;
using Scada.Server.Modules.DBExport;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Threading;
using Utils;

namespace Scada.Server.Modules
{
    /// <summary>
    /// Server module logic
    /// <para>Логика работы серверного модуля</para>
    /// </summary>
    public class ModDBExportLogic : ModLogic
    {
        /// <summary>
        /// Экспортёр для одного назначения экспорта
        /// </summary>
        private class Exporter
        {
            private Log log; // журнал работы модуля

            /// <summary>
            /// Конструктор, ограничивающий создание объекта без параметров
            /// </summary>
            private Exporter()
            {
            }
            /// <summary>
            /// Конструктор
            /// </summary>
            public Exporter(Config.ExportDestination expDest, Log log)
            {
                if (expDest == null)
                    throw new ArgumentNullException("expDest");

                this.log = log;
                DataSource = expDest.DataSource;
                ExportParams = expDest.ExportParams;
            }

            /// <summary>
            /// Получить источник данных
            /// </summary>
            public DataSource DataSource { get; private set; }
            /// <summary>
            /// Получить параметры экспорта
            /// </summary>
            public Config.ExportParams ExportParams { get; private set; }

            /// <summary>
            /// Получить признак, что работа экспортёра завершена
            /// </summary>
            public bool Terminated { get; private set; }

            /// <summary>
            /// Запустить работу экспортёра
            /// </summary>
            public void Start()
            {

            }
            /// <summary>
            /// Начать остановку работы экспортёра
            /// </summary>
            public void Terminate()
            {

            }
            /// <summary>
            /// Прервать работу экспортёра
            /// </summary>
            public void Abort()
            {

            }
            /// <summary>
            /// Добавить текущие данные в очередь экспорта
            /// </summary>
            public void EnqueueCurData(SrezTableLight.Srez curSrez)
            {
            }
            /// <summary>
            /// Добавить архивные данные в очередь экспорта
            /// </summary>
            public void EnqueueArcData(SrezTableLight.Srez arcSrez)
            {
            }
            /// <summary>
            /// Добавить событие в очередь экспорта
            /// </summary>
            public void EnqueueEvent(EventTableLight.Event ev)
            {
            }
            /// <summary>
            /// Получить информацию о работе экспортёра
            /// </summary>
            public string GetInfo()
            {
                return "";
            }
        }


        /// <summary>
        /// Имя файла журнала работы модуля
        /// </summary>
        internal const string LogFileName = "ModDBExport.log";
        /// <summary>
        /// Имя файла информации о работе модуля
        /// </summary>
        private const string InfoFileName = "ModDBExport.txt";
        /// <summary>
        /// Задержка потока обновления файла информации, мс
        /// </summary>
        private const int InfoThreadDelay = 500;
        /// <summary>
        /// Формат текста информации о работе модуля
        /// </summary>
        private static readonly string ModInfoFormat = Localization.UseRussian ?
            "Модуль экспорта данных" + Environment.NewLine +
            "----------------------" + Environment.NewLine +
            "Состояние: {0}" + Environment.NewLine + Environment.NewLine +
            "Источники данных" + Environment.NewLine +
            "----------------" + Environment.NewLine :
            "Export Data Module" + Environment.NewLine +
            "------------------" + Environment.NewLine +
            "State: {0}" + Environment.NewLine + Environment.NewLine +
            "Data Sources" + Environment.NewLine +
            "------------" + Environment.NewLine;

        private bool normalWork;          // признак нормальной работы модуля
        private string workState;         // строковая запись состояния работы
        private Log log;                  // журнал работы модуля
        private string infoFileName;      // полное имя файла информации
        private Thread infoThread;        // поток для обновления файла информации
        private Config config;            // конфигурация модуля
        private List<Exporter> exporters; // экспортёры


        /// <summary>
        /// Конструктор
        /// </summary>
        public ModDBExportLogic()
        {
            normalWork = true;
            workState = Localization.UseRussian ? "норма" : "normal";
            log = null;
            infoFileName = "";
            infoThread = null;
            config = null;
            exporters = null;
        }


        /// <summary>
        /// Получить имя модуля
        /// </summary>
        public override string Name
        {
            get
            {
                return "ModDBExport";
            }
        }


        /// <summary>
        /// Разъединиться с БД с выводом возможной ошибки в журнал
        /// </summary>
        private void Disconnect(DataSource dataSource)
        {
            try
            {
                dataSource.Disconnect();
            }
            catch (Exception ex)
            {
                log.WriteAction(string.Format(Localization.UseRussian ? "Ошибка при разъединении с БД {0}: {1}" :
                    "Error disconnecting from DB {0}: {1}", dataSource.Name, ex.Message));
            }
        }

        /// <summary>
        /// Экспортировать срез в БД
        /// </summary>
        private void ExportSrez(DataSource dataSource, DbCommand cmd, int[] cnlNums, SrezTableLight.Srez srez)
        {
            foreach (int cnlNum in cnlNums)
            {
                SrezTableLight.CnlData cnlData;

                if (srez.GetCnlData(cnlNum, out cnlData))
                {
                    dataSource.SetCmdParam(cmd, "cnlNum", cnlNum);
                    dataSource.SetCmdParam(cmd, "val", cnlData.Val);
                    dataSource.SetCmdParam(cmd, "stat", cnlData.Stat);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Создать срез с заданными номерами каналов, используя данные из исходного среза
        /// </summary>
        private SrezTableLight.Srez CreateSrez(DateTime srezDT, int[] cnlNums, SrezTableLight.Srez sourceSrez)
        {
            int cnlCnt = cnlNums.Length;
            SrezTableLight.Srez srez = new SrezTableLight.Srez(srezDT, cnlCnt);

            for (int i = 0; i < cnlCnt; i++)
            {
                int cnlNum = cnlNums[i];
                SrezTableLight.CnlData cnlData;
                sourceSrez.GetCnlData(cnlNum, out cnlData);

                srez.CnlNums[i] = cnlNum;
                srez.CnlData[i] = cnlData;
            }

            return srez;
        }

        /// <summary>
        /// Записать в файл информацию о работе модуля
        /// </summary>
        private void WriteInfo()
        {
            try
            {
                // формирование текста
                StringBuilder sbInfo = new StringBuilder();
                sbInfo.AppendLine(string.Format(ModInfoFormat, workState));

                int cnt = exporters.Count;
                if (cnt > 0)
                {
                    for (int i = 0; i < cnt; i++)
                        sbInfo.Append((i + 1).ToString()).Append(". ").
                            AppendLine(exporters[i].GetInfo());
                }
                else
                {
                    sbInfo.AppendLine(Localization.UseRussian ? "Нет" : "No");
                }

                // вывод в файл
                using (StreamWriter writer = new StreamWriter(infoFileName, false, Encoding.UTF8))
                    writer.Write(sbInfo.ToString());
            }
            catch (ThreadAbortException)
            {
            }
            catch (Exception ex)
            {
                log.WriteAction(ModPhrases.WriteInfoError + ": " + ex.Message, Log.ActTypes.Exception);
            }
        }


        /// <summary>
        /// Выполнить действия при запуске работы сервера
        /// </summary>
        public override void OnServerStart()
        {
            // вывод в журнал
            log = new Log(Log.Formats.Simple);
            log.Encoding = Encoding.UTF8;
            log.FileName = LogDir + LogFileName;
            log.WriteBreak();
            log.WriteAction(string.Format(ModPhrases.StartModule, Name));

            // определение полного имени файла информации
            infoFileName = LogDir + InfoFileName;

            // загрука конфигурации
            config = new Config(ConfigDir);
            string errMsg;

            if (config.Load(out errMsg))
            {
                // инициализация источников данных
                int i = 0;
                while (i < config.ExportDestinations.Count)
                {
                    Config.ExportDestination expDest = config.ExportDestinations[i];
                    DataSource dataSource = expDest.DataSource;
                    Config.ExportParams expParams = expDest.ExportParams;

                    try
                    {
                        dataSource.InitConnection();
                        dataSource.InitCommands(
                            expParams.ExportCurData ? expParams.ExportCurDataQuery : "",
                            expParams.ExportArcData ? expParams.ExportArcDataQuery : "", 
                            expParams.ExportEvent ? expParams.ExportEventQuery : "");
                        i++;
                    }
                    catch (Exception ex)
                    {
                        log.WriteAction(string.Format(Localization.UseRussian ? 
                            "Ошибка при инициализации источника данных {0}: {1}" : 
                            "Error initializing data source {0}: {1}", dataSource.Name, ex.Message));
                        // исключение из работы назначения, источник данных которого не был успешно инициализирован
                        config.ExportDestinations.RemoveAt(i);
                    }
                }

                // создание и запуск экспортёров
                exporters = new List<Exporter>();
                foreach (Config.ExportDestination expDest in config.ExportDestinations)
                {
                    Exporter exporter = new Exporter(expDest, log);
                    exporter.Start();
                    exporters.Add(exporter);
                }

                // создание и запуск потока для обновления файла информации
                infoThread = new Thread(() => { while (true) { WriteInfo(); Thread.Sleep(InfoThreadDelay); } });
                infoThread.Start();
            }
            else
            {
                normalWork = false;
                workState = Localization.UseRussian ? "ошибка" : "error";
                WriteInfo();
                log.WriteAction(errMsg);
                log.WriteAction(ModPhrases.NormalModExecImpossible);
            }
        }

        /// <summary>
        /// Выполнить действия при остановке работы сервера
        /// </summary>
        public override void OnServerStop()
        {
            // разъединение с БД
            foreach (Config.ExportDestination expDest in config.ExportDestinations)
                Disconnect(expDest.DataSource);

            // остановка экспортёров
            foreach (Exporter exporter in exporters)
                exporter.Terminate();

            // ожидание завершения работы экспортёров
            DateTime nowDT = DateTime.Now;
            DateTime begDT = nowDT;
            DateTime endDT = nowDT.AddMilliseconds(WaitForStop);
            bool running;

            do
            {
                running = false;
                foreach (Exporter exporter in exporters)
                {
                    if (!exporter.Terminated)
                    {
                        running = true;
                        break;
                    }
                }
                if (running)
                    Thread.Sleep(ScadaUtils.ThreadDelay);
                nowDT = DateTime.Now;
            }
            while (begDT <= nowDT && nowDT <= endDT && running);

            // прерывание работы экспортёров
            if (running)
            {
                foreach (Exporter exporter in exporters)
                    if (!exporter.Terminated)
                        exporter.Abort();
            }

            // прерывание потока для обновления файла информации
            if (infoThread != null)
            {
                infoThread.Abort();
                infoThread = null;
            }

            // вывод информации
            WriteInfo();
            log.WriteAction(string.Format(ModPhrases.StopModule, Name));
            log.WriteBreak();
        }

        /// <summary>
        /// Выполнить действия после обработки новых текущих данных
        /// </summary>
        public override void OnCurDataProcessed(int[] cnlNums, SrezTableLight.Srez curSrez)
        {
            // экспорт текущих данных в БД
            if (normalWork)
            {
                // создание экпортируемого среза
                SrezTableLight.Srez srez = CreateSrez(DateTime.Now, cnlNums, curSrez);

                // добавление среза в очереди экспорта
                foreach (Exporter exporter in exporters)
                    exporter.EnqueueCurData(srez);

                // устарело:
                /*foreach (Config.ExportDestination expDest in config.ExportDestinations)
                {
                    if (expDest.ExportParams.ExportCurData)
                    {
                        DataSource dataSource = expDest.DataSource;

                        try
                        {
                            dataSource.Connect();
                            dataSource.SetCmdParam(dataSource.ExportCurDataCmd, "dateTime", DateTime.Now);
                            ExportSrez(dataSource, dataSource.ExportCurDataCmd, cnlNums, curSrez);
                        }
                        catch (Exception ex)
                        {
                            log.WriteAction(string.Format(Localization.UseRussian ? 
                                "Ошибка при экспорте текущих данных в БД {0}: {1}" :
                                "Error export current data to DB {0}: {1}", dataSource.Name, ex.Message));
                        }
                        finally
                        {
                            Disconnect(dataSource);
                        }
                    }
                }*/
            }
        }

        /// <summary>
        /// Выполнить действия после обработки новых архивных данных
        /// </summary>
        public override void OnArcDataProcessed(int[] cnlNums, SrezTableLight.Srez arcSrez)
        {
            // экспорт архивных данных в БД
            if (normalWork)
            {
                // создание экпортируемого среза
                SrezTableLight.Srez srez = CreateSrez(arcSrez.DateTime, cnlNums, arcSrez);

                // добавление среза в очереди экспорта
                foreach (Exporter exporter in exporters)
                    exporter.EnqueueArcData(srez);

                // устарело:
                /*foreach (Config.ExportDestination expDest in config.ExportDestinations)
                {
                    if (expDest.ExportParams.ExportArcData)
                    {
                        DataSource dataSource = expDest.DataSource;

                        try
                        {
                            dataSource.Connect();
                            dataSource.SetCmdParam(dataSource.ExportArcDataCmd, "dateTime", arcSrez.DateTime);
                            ExportSrez(dataSource, dataSource.ExportArcDataCmd, cnlNums, arcSrez);
                        }
                        catch (Exception ex)
                        {
                            log.WriteAction(string.Format(Localization.UseRussian ?
                                "Ошибка при экспорте архивных данных в БД {0}: {1}" :
                                "Error export current data to DB {0}: {1}", dataSource.Name, ex.Message));
                        }
                        finally
                        {
                            Disconnect(dataSource);
                        }
                    }
                }*/
            }
        }

        /// <summary>
        /// Выполнить действия после создания события и записи на диск
        /// </summary>
        public override void OnEventCreated(EventTableLight.Event ev)
        {
            // экспорт события в БД
            if (normalWork)
            {
                // добавление события в очереди экспорта
                foreach (Exporter exporter in exporters)
                    exporter.EnqueueEvent(ev);

                // устарело:
                /*foreach (Config.ExportDestination expDest in config.ExportDestinations)
                {
                    if (expDest.ExportParams.ExportEvent)
                    {
                        DataSource dataSource = expDest.DataSource;

                        try
                        {
                            dataSource.Connect();
                            DbCommand cmd = dataSource.ExportEventCmd;
                            dataSource.SetCmdParam(cmd, "dateTime", ev.DateTime);
                            dataSource.SetCmdParam(cmd, "objNum", ev.ObjNum);
                            dataSource.SetCmdParam(cmd, "kpNum", ev.KPNum);
                            dataSource.SetCmdParam(cmd, "paramID", ev.ParamID);
                            dataSource.SetCmdParam(cmd, "cnlNum", ev.CnlNum);
                            dataSource.SetCmdParam(cmd, "oldCnlVal", ev.OldCnlVal);
                            dataSource.SetCmdParam(cmd, "oldCnlStat", ev.OldCnlStat);
                            dataSource.SetCmdParam(cmd, "newCnlVal", ev.NewCnlVal);
                            dataSource.SetCmdParam(cmd, "newCnlStat", ev.NewCnlStat);
                            dataSource.SetCmdParam(cmd, "checked", ev.Checked);
                            dataSource.SetCmdParam(cmd, "userID", ev.UserID);
                            dataSource.SetCmdParam(cmd, "descr", ev.Descr);
                            dataSource.SetCmdParam(cmd, "data", ev.Data);
                            cmd.ExecuteNonQuery();
                        }
                        catch (Exception ex)
                        {
                            log.WriteAction(string.Format(Localization.UseRussian ?
                                "Ошибка при экспорте события в БД {0}: {1}" :
                                "Error export event to DB {0}: {1}", dataSource.Name, ex.Message));
                        }
                        finally
                        {
                            Disconnect(dataSource);
                        }
                    }
                }*/
            }
        }
    }
}
