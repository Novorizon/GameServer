using System.Collections.Generic;
using System;

namespace DataBase
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public class TableAccessAttribute : Attribute
    {

        public TableAccessAttribute()
        {
        }
    }

}