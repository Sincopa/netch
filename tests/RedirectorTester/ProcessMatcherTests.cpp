#include "../../src/native/Redirector/ProcessMatcher.h"
#include <cassert>
#include <iostream>

int main()
{
    assert(MatchProcessName(LR"(C:\Users\user\AppData\Local\Discord\app-1.0.9256\Discord.exe)", LR"(discord\.exe)"));
    assert(MatchProcessName(LR"(C:\APPS\DISCORD.EXE)", LR"(discord\.exe)"));
    assert(MatchProcessName(LR"(C:\Apps\chrome.exe)", LR"(chrome\.exe)"));
    assert(!MatchProcessName(LR"(C:\Apps\telegram.exe)", LR"(discord\.exe)"));
    assert(!MatchProcessName(LR"(C:\Apps\discordXexe)", LR"(discord\.exe)"));
    std::cout << "Process matcher: 5 checks passed\n";
}
