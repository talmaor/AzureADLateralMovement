using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AzureActiveDirectoryApplication.Utils
{
    public static class ApplicationStateExtension
    {
        public static T GetSetApplicationState<T>(this HttpApplicationState appState, string objectName, object objectValue = null, int syncCheckMinutes = 0)
        {
            T retVal = default(T);
            appState.Lock();
            if (appState[objectName + "LastSync"] == null || DateTime.Now.Subtract(((DateTime)appState[objectName + "LastSync"])).TotalMinutes >= syncCheckMinutes)
            {
                appState[objectName + "LastSync"] = DateTime.Now;

                if (objectValue != null)
                    appState[objectName] = objectValue;
            }
            if (appState[objectName] != null)
                retVal = (T)appState[objectName];
            appState.UnLock();
            return retVal;
        }
        public static object GetSetApplicationState(this HttpApplicationState appState, string objectName, object objectValue = null, int syncCheckMinutes = 0)
        {
            object retVal = null;
            appState.Lock();
            if (appState[objectName + "LastSync"] == null || DateTime.Now.Subtract(((DateTime)appState[objectName + "LastSync"])).TotalMinutes >= syncCheckMinutes)
            {
                appState[objectName + "LastSync"] = DateTime.Now;

                if (objectValue != null)
                    appState[objectName] = objectValue;
            }
            if (appState[objectName] != null)
                retVal = appState[objectName];
            appState.UnLock();
            return retVal;
        }
        public static void SetApplicationState(this HttpApplicationState appState, string objectName, object objectValue, int syncCheckMinutes = 0)
        {
            appState.Lock();
            if (appState[objectName + "LastSync"] == null || DateTime.Now.Subtract(((DateTime)appState[objectName + "LastSync"])).TotalMinutes >= syncCheckMinutes)
            {
                appState[objectName + "LastSync"] = DateTime.Now;
                appState[objectName] = objectValue;
            }
            appState.UnLock();
        }
        public static object GetApplicationState(this HttpApplicationState appState, string objectName)
        {
            object retVal = null;
            appState.Lock();
            if (appState[objectName] != null)
                retVal = appState[objectName];
            appState.UnLock();
            return retVal;
        }
        public static T GetApplicationState<T>(this HttpApplicationState appState, string objectName)
        {
            T retVal = default(T);
            appState.Lock();
            if (appState[objectName] != null)
                retVal = (T)appState[objectName];
            appState.UnLock();
            return retVal;
        }
    }
}