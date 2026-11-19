// Apply a name plan written by tools/ghidra_port_names.py to the current program.
//
// Argument: the plan's path. ghidra-mcp splits arguments on spaces, so the path must hold none.
// The plan names its program, and the script refuses any other. For each entry it names the function at the
// address, creating one where the plan says the address is a function entry and Ghidra has none. A function that
// already has a name of its own keeps it, and a function that already has a plate comment keeps that.
// Usage: docs/tools-reference.md, "Native engine RE (Ghidra)".
//@category OME

import java.io.Reader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.SourceType;
import ghidra.util.exception.DuplicateNameException;

public class ApplyFunctionNames extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] arguments = getScriptArgs();
        if (arguments.length != 1) {
            println("OMEMARK error: expected one argument, the plan's path");
            return;
        }
        JsonObject plan;
        try (Reader reader = Files.newBufferedReader(Paths.get(arguments[0]), StandardCharsets.UTF_8)) {
            plan = JsonParser.parseReader(reader).getAsJsonObject();
        }
        String program = currentProgram.getDomainFile().getPathname();
        if (!program.equals(plan.get("program").getAsString())) {
            println("OMEMARK error: the plan is for " + plan.get("program").getAsString() + ", not " + program);
            return;
        }
        int named = 0, kept = 0, created = 0, missing = 0, plated = 0, renamedForClash = 0;
        JsonArray functions = plan.getAsJsonArray("functions");
        for (JsonElement element : functions) {
            JsonObject entry = element.getAsJsonObject();
            Address address = toAddr(Long.decode(entry.get("address").getAsString()));
            Function function = getFunctionAt(address);
            if (function == null) {
                if (getFunctionContaining(address) == null) {
                    disassemble(address);
                    function = createFunction(address, null);
                }
                if (function == null) {
                    missing++;
                    println("OMEMARK no function at " + address + " for " + entry.get("name").getAsString());
                    continue;
                }
                created++;
            }
            if (function.getSymbol().getSource() == SourceType.DEFAULT) {
                String name = entry.get("name").getAsString();
                try {
                    function.setName(name, SourceType.USER_DEFINED);
                } catch (DuplicateNameException clash) {
                    function.setName(name + "_" + Long.toHexString(address.getOffset()), SourceType.USER_DEFINED);
                    renamedForClash++;
                }
                named++;
            } else {
                kept++;
            }
            if (entry.has("plate") && function.getComment() == null) {
                function.setComment(entry.get("plate").getAsString());
                plated++;
            }
        }
        println("OMEMARK applied to " + program + ": " + named + " named (" + renamedForClash + " with an address "
                + "suffix for a name clash), " + kept + " kept their own name, " + plated + " plates set, " + created
                + " functions created, " + missing + " addresses without a function");
    }
}
