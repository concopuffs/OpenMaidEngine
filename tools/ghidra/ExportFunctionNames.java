// Write the current program's named functions as JSON, for tools/ghidra_port_names.py.
//
// Argument: the output path. ghidra-mcp splits arguments on spaces, so the path must hold none.
// A function is written when its name is not Ghidra's default (FUN_...) or it has a plate comment.
// Usage: docs/tools-reference.md, "Native engine RE (Ghidra)".
//@category OME

import java.io.OutputStreamWriter;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.symbol.SourceType;

public class ExportFunctionNames extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] arguments = getScriptArgs();
        if (arguments.length != 1) {
            println("OMEMARK error: expected one argument, the output path");
            return;
        }
        JsonArray functions = new JsonArray();
        int total = 0;
        FunctionIterator iterator = currentProgram.getFunctionManager().getFunctions(true);
        while (iterator.hasNext()) {
            Function function = iterator.next();
            total++;
            boolean named = function.getSymbol().getSource() != SourceType.DEFAULT;
            String plate = function.getComment();
            if (!named && plate == null) {
                continue;
            }
            JsonObject entry = new JsonObject();
            entry.addProperty("address", "0x" + Long.toHexString(function.getEntryPoint().getOffset()));
            entry.addProperty("name", function.getName());
            entry.addProperty("named", named);
            if (plate != null) {
                entry.addProperty("plate", plate);
            }
            functions.add(entry);
        }
        JsonObject root = new JsonObject();
        root.addProperty("program", currentProgram.getDomainFile().getPathname());
        root.addProperty("function_count", total);
        root.add("functions", functions);
        try (Writer writer = new OutputStreamWriter(Files.newOutputStream(Paths.get(arguments[0])),
                                                    StandardCharsets.UTF_8)) {
            new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create().toJson(root, writer);
        }
        println("OMEMARK exported " + functions.size() + " of " + total + " functions of "
                + currentProgram.getDomainFile().getPathname() + " to " + arguments[0]);
    }
}
