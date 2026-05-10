using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Cqrs.Abstractions.Cache;

public interface ICacheKeyService
{
    string BuildKey(string customKey);
}
