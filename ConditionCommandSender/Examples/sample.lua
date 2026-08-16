-- ConditionCommandSender Lua example
print_ccs("Lua started")
command("/echo CCS Lua test")

if is_cancelled() then
    return
end

-- CCS commands can also be executed from Lua:
-- command('/ccs on "Rule Name"')
-- command('/ccs off "Rule Name"')
