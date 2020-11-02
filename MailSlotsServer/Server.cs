using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;

namespace MailSlots
{
    public partial class frmMain : Form
    {
        private int serverMS;       // дескриптор мэйлслота
        private int regClientMS;       // дескриптор мэйлслота
        private Thread t1;                       // поток для обслуживания мэйлслота
        private Thread t2;                       // поток для обслуживания мэйлслота
        private bool _continue = true;          // флаг, указывающий продолжается ли работа с мэйлслотом
        private List<string> clients;
        string regServer = "R";
        string connetcionString = "\\\\.\\mailslot\\ServerMailslot";
        
        // конструктор формы
        public frmMain()
        {
            InitializeComponent();

            clients = new List<string>();

            // создание мэйлслота
            serverMS = 
                DIS.Import.CreateMailslot(
                connetcionString, 
                0, 
                DIS.Types.MAILSLOT_WAIT_FOREVER,
                0);
            regClientMS = 
                DIS.Import.CreateMailslot(
                connetcionString+ regServer,
                0, 
                DIS.Types.MAILSLOT_WAIT_FOREVER,
                0);

            // создание потока, отвечающего за работу с мэйлслотом
            t1 = new Thread(ReceiveMessage);
            t1.Start();
            t2 = new Thread(ReceiveMessageToRegisterorUn);
            t2.Start();
        }

        private void ReceiveMessage()
        {
            string msg = "";            // прочитанное сообщение
            int MailslotSize = 0;       // максимальный размер сообщения
            int lpNextSize = 0;         // размер следующего сообщения
            int MessageCount = 0;       // количество сообщений в мэйлслоте
            uint realBytesReaded = 0;   // количество реально прочитанных из мэйлслота байтов
            string prevmes = string.Empty;
            // входим в бесконечный цикл работы с мэйлслотом
            while (_continue)
            {
                // получаем информацию о состоянии мэйлслота
                DIS.Import.GetMailslotInfo(serverMS,
                    MailslotSize,
                    ref lpNextSize,
                    ref MessageCount, 
                    0);
                // если есть сообщения в мэйлслоте, то обрабатываем каждое из них
                for (int i = 0; i < MessageCount; i++)
                {
                    byte[] buff = new byte[1024];                          
                    DIS.Import.FlushFileBuffers(serverMS);      // "принудительная" запись данных, расположенные в буфере операционной системы, в файл мэйлслота
                    DIS.Import.ReadFile(serverMS,
                        buff,
                        1024, 
                        ref realBytesReaded,
                        0);      // считываем последовательность байтов из мэйлслота в буфер buff
                    msg = Encoding.Unicode.GetString(buff);                 // выполняем преобразование байтов в последовательность символов
                    if (prevmes.Equals(msg))
                        continue;
                    if (string.IsNullOrEmpty(msg))
                        break;
                    rtbMessages.Invoke((MethodInvoker)delegate
                    {
                        if (msg != "")
                            rtbMessages.Text += "\n >> " + msg + " \n";     // выводим полученное сообщение на форму
                    });
                    //Отправляем клиентам
                    foreach (string connection in clients)
                    {
                        int mailServerSlot = DIS.Import.CreateFile
                            (@"\\.\mailslot\"+connection,
                            DIS.Types.EFileAccess.GenericWrite, 
                            DIS.Types.EFileShare.Read, 
                            0,
                            DIS.Types.ECreationDisposition.OpenExisting,
                            0, 
                            0);

                        if (mailServerSlot != -1)
                        {
                            uint BytesWritten = 0;  // количество реально записанных в мэйлслот байт
                            byte[] buff1 = Encoding.Unicode.GetBytes(msg);    // выполняем преобразование сообщения (вместе с идентификатором машины) в последовательность байт
                            DIS.Import.WriteFile(mailServerSlot,
                                buff1,
                                Convert.ToUInt32(buff1.Length),
                                ref BytesWritten,
                                0);
                        }
                    }
                    prevmes = msg;
                }
            }
        }

        private void ReceiveMessageToRegisterorUn()
        {
            string msg = "";            // прочитанное сообщение
            int MailslotSize = 0;       // максимальный размер сообщения
            int lpNextSize = 0;         // размер следующего сообщения
            int MessageCount = 0;       // количество сообщений в мэйлслоте
            uint realBytesReaded = 0;   // количество реально прочитанных из мэйлслота байтов
            string previousMessage = string.Empty;
            // входим в бесконечный цикл работы с мэйлслотом
            while (_continue)
            {
                // получаем информацию о состоянии мэйлслота
                DIS.Import.GetMailslotInfo(regClientMS,
                    MailslotSize,
                    ref lpNextSize,
                    ref MessageCount,
                    0);
                // если есть сообщения в мэйлслоте, то обрабатываем каждое из них
                for (int i = 0; i < MessageCount; i++)
                {
                    byte[] buff = new byte[1024];
                    DIS.Import.FlushFileBuffers(regClientMS);      // "принудительная" запись данных, расположенные в буфере операционной системы, в файл мэйлслота
                    DIS.Import.ReadFile(regClientMS,
                        buff,
                        1024,
                        ref realBytesReaded,
                        0);      // считываем последовательность байтов из мэйлслота в буфер buff
                    msg = Encoding.Unicode.GetString(buff);                 // выполняем преобразование байтов в последовательность символов
                    if (previousMessage.Equals(msg))
                        continue;
                    if (string.IsNullOrEmpty(msg))
                        break;
                    string client = msg.Remove(0, 1);
                    if (msg[0]=='R')
                    {
                        clients.Add(client);
                    }
                    else
                    {
                        clients.Remove(client);
                    }
                    previousMessage = msg;
                }
            }
        }

        private void frmMainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (clients.Count > 0)
            {
                e.Cancel = true;
            }
            else
            {
                _continue = false;
                if (t1 != null)
                {
                    t1.Interrupt();
                    t1.Join();
                }

                if (serverMS != -1)
                    DIS.Import.CloseHandle(serverMS);

                if (t2 != null)
                {
                    t2.Interrupt();
                    t2.Join();
                }

                if (regClientMS != -1)
                    DIS.Import.CloseHandle(regClientMS);
            }
        }

        private void rtbMessages_TextChanged(object sender, EventArgs e)
        {

        }
    }
}