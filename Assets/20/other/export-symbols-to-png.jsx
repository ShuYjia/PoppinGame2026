/*
 *  Export all symbols to PNG
 *  https://github.com/hamdirizal/export-symbols-to-png/
 *  
 *
 *  Export all symbols in the "Symbols" panel to png files located in the "exported" subfolder relative to the AI file. *
 * 
 *  Script by me, Hamdi Rizal (hamdirizal [at] gmail [dot] com)
 *  based on script I found online, but currently I cannot find the original poster. Will put it here when I found the original script again.
 *
 *  My version:
 *  - Changes the default export location to "exported" folder relative to the document location instead of selecting a directory, 
 *  - Create subfolder based on the symbol names. Eg: if you name the symbol "ui/btn/play" then it will export to play.png to "ui/btn" subfolder.
 *  - Exclude export symbols prefixed with an _ (underscore).
 *
 *  JavaScript Script for Adobe Illustrator CS6.
 *  Tested with Adobe Illustrator CS6 16.0.0, Windows 7.
 *  This script provided "as is" without warranty of any kind.
 *  Free to use and distribute.
 *
 *  Copyright (c) 2017 Hamdi Rizal
 *
 *  
 *
 */


#target illustrator

var doc = app.activeDocument;
var symbolCount = doc.symbols.length;

var selectedPrefix = "";  //export only symbols started with this string. to export all leave it as an empty string "".
var scale = 100; //The scale of exported png in percent(%)
var exportPath = doc.path + "/"+doc.name+"-exported"; //The export path is "/exported" subfolder relative to the ai file


var layers = doc.layers; //The layers in the active document. Sorted from top to bottom.
var layerVisibilities = []; //Store the layer visibilities here



hideAllLayers();
exportSymbols();
showLayers();




/*
 * Store all layers visibilities in order. And hide all the layers.
 */
function hideAllLayers(){
    for(var i=0;i<layers.length;i++){
        layerVisibilities.push(layers[i].visible);
        layers[i].visible= false;
    }
}




/*
 * Show layers again, just like before they hidden.
 */
function showLayers(){
    for(var i=0;i<layerVisibilities.length;i++){
        layers[i].visible= layerVisibilities[i];
    }
}




/*
 * Export all the symbols
 */
function exportSymbols(){
    //Loop all the symbols in the library
    if (symbolCount >= 1) {   

            // create temp layer
            doc.layers.add();
           
            //Read directory
            var rootDir = new Folder(exportPath);

            // create directory
            rootDir.create();


            

            // loop through symbols
            for (var i = 0; i < doc.symbols.length; i++) {
                // assign name
                var symbolName = (doc.symbols[i].name)
                var filename = symbolName;
                
                // Set the default export directory to rootDir
                var exportDir = rootDir;

                //If symbolName is not started with _ or -, and started with the selected prefix
                if(symbolName[0]!=="_" && symbolName[0]!=="-" && symbolName.substring(0,selectedPrefix.length)==selectedPrefix){
                    
                    // put temporary symbol instance in the artboard
                    var tempSymbol = doc.symbolItems.add(doc.symbols[i]);
                    tempSymbol.left = 0;
                    tempSymbol.top =0;

                    //Check if the name has a "/"
                    var segments = symbolName.split('/');

                    //If the symbol name contains "/", change export dir
                    if(segments.length>1){

                        var tempDirName=rootDir+"/";
                        var j=0;
                        for(j=0;j<segments.length-1;j++)
                        {
                            tempDirName+=segments[j]+"/";
                        }
                        
                        //Create a folder from a path string
                        var subDir = new Folder(tempDirName);

                        //Really create a folder
                        subDir.create();

                        //Use the last segment as the filename
                        filename = segments[segments.length-1];
                        
                        //Change the export dir to use the subDir
                        exportDir = subDir;
                    }

                    // export symbols
                    savePNG(exportDir, filename);

                    // delete temp symbol instance
                    tempSymbol.remove();
                }

            }
            // remove temp layer
            doc.layers[0].remove();
        

    } else {
        alert("You don't have any symbols in this document");
    }
}




/*
 * Save current view to a PNG file
 */
function savePNG(dest, filename) {
        // save options
        var type = ExportType.PNG24;
        var options = new ExportOptionsPNG24();
        options.transparency = true;
        options.horizontalScale=scale;
        options.verticalScale=scale;

        // file
        var file = new File(dest + "/" + filename);

        // export
        doc.exportFile(file, type, options);
    }