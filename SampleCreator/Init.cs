﻿using Serilog;
using System.IO;
using Xbim.Common;

namespace SampleCreator
{
    class Init
    {
        public static void SetLogger(string path)
        {
            path = Path.ChangeExtension(path, ".export.log");
            //if (File.Exists(path))
            //    File.Delete(path);

            var config = new LoggerConfiguration()
               .Enrich.FromLogContext()
               .WriteTo.File(path);

            // set up default logger
            Log.Logger = config.CreateLogger();

            // set up logging for the model
            XbimLogging.LoggerFactory.AddSerilog();
        }
    }
}