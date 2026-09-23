#pragma once
#include <regex>
#include <string>

// Windows executable paths are case-insensitive, including paths from the kernel.
inline bool MatchProcessName(const std::wstring& name, const std::wstring& rule)
{
    return std::regex_search(name, std::wregex(rule, std::regex_constants::icase));
}
