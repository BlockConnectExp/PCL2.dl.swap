#pragma once

#include "Profiler.h"

struct PlanEntry
{
    std::wstring moduleMvid;
    mdToken targetToken = 0;
    int hookIndex = 0;                  // 0 download, 1 OTA, 2 permission
    bool hasThis = false;
    int argCount = 0;
    bool returnsValue = false;
};

// data\rewrite-plan.tsv, produced by the managed payload from data\offset-manifest.json.
// Columns: moduleMvid  targetToken  hookIndex  hasThis  argCount  returnsValue  methodName
std::vector<PlanEntry> LoadRewritePlan();


<!-- GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover -->