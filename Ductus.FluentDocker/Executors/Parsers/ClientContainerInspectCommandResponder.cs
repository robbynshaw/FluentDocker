﻿using System.Text;
using Ductus.FluentDocker.Model.Containers;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientContainerInspectCommandResponder : IProcessResponseParser<Container>
  {
    public CommandResponse<Container> Response { get; private set; }

    public IProcessResponse<Container> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "Empty response", new Container());
        return this;
      }

      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse(new Container());
        return this;
      }


      var arr = response.StdOutAsArry;
      var sb = new StringBuilder();
      for (var i = 1; i < arr.Length - 1; i++)
      {
        sb.AppendLine(arr[i]);
      }

      var container = sb.ToString();
      var obj = JsonConvert.DeserializeObject<Container>(container);

      if (!string.IsNullOrEmpty(obj.Name) && obj.Name.StartsWith("/")) obj.Name = obj.Name.Substring(1);
      
      Response = response.ToResponse(true, string.Empty, obj);
      return this;
    }
  }
}