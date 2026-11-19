// Decompile a list of functions of the current program into one text file.
//
// Arguments: the output path, then one or more addresses (0x...). ghidra-mcp splits arguments on spaces, so the
// path must hold none. Each function is written under a line "// ===== <address> <name>", with its plate comment
// as the decompiler shows it. An address inside a function gives that function. Reading a file keeps a session
// out of ghidra-mcp's long results, which carry every script's compile output.
// Usage: docs/tools-reference.md, "Native engine RE (Ghidra)".
//@category OME

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class DecompileToFile extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] arguments = getScriptArgs();
        if (arguments.length < 2) {
            println("OMEMARK error: expected the output path, then addresses");
            return;
        }
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        StringBuilder text = new StringBuilder();
        int written = 0;
        for (int k = 1; k < arguments.length; k++) {
            Address address = toAddr(Long.decode(arguments[k]));
            Function function = getFunctionAt(address);
            if (function == null) {
                function = getFunctionContaining(address);
            }
            if (function == null) {
                text.append("// ===== ").append(arguments[k]).append(" no function\n\n");
                continue;
            }
            DecompileResults results = decompiler.decompileFunction(function, 120, monitor);
            text.append("// ===== 0x").append(Long.toHexString(function.getEntryPoint().getOffset())).append(' ')
                .append(function.getName()).append('\n');
            if (results.decompileCompleted()) {
                text.append(results.getDecompiledFunction().getC().replace("\r\n", "\n"));
                written++;
            } else {
                text.append("// decompile failed: ").append(results.getErrorMessage()).append('\n');
            }
            text.append('\n');
        }
        decompiler.dispose();
        Files.write(Paths.get(arguments[0]), text.toString().getBytes(StandardCharsets.UTF_8));
        println("OMEMARK decompiled " + written + " of " + (arguments.length - 1) + " functions of "
                + currentProgram.getDomainFile().getPathname() + " to " + arguments[0]);
    }
}
