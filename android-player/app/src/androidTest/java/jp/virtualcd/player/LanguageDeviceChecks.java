package jp.virtualcd.player;

import android.app.*;
import android.content.*;
import android.graphics.Bitmap;
import android.view.*;
import java.io.*;
import java.util.concurrent.atomic.AtomicReference;

/** Uses the actual language selector. Does not modify music, favorites, or sync configuration. */
final class LanguageDeviceChecks {
    static void run(Instrumentation test)throws Exception{
        var context=test.getTargetContext();
        String original=LanguageStrings.code();
        Activity activity=test.startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        var method=MainActivity.class.getDeclaredMethod("showLanguageSettings");method.setAccessible(true);
        try{
            for(String code:new String[]{"en","ja"}){
                if(code.equals(LanguageStrings.code()))continue;
                final Activity current=activity;
                AtomicReference<AlertDialog> selected=new AtomicReference<>();
                for(int tries=0;tries<60&&selected.get()==null;tries++){
                    test.runOnMainSync(()->{try{selected.set((AlertDialog)method.invoke(current));}catch(Exception e){throw new RuntimeException(e);}});
                    if(selected.get()==null)Thread.sleep(500);
                }
                if(selected.get()==null)throw new AssertionError("Language selector stayed busy");
                var dialog=selected.get();int index=code.equals("en")?1:0;
                test.runOnMainSync(()->dialog.getListView().performItemClick(null,index,index));
                test.waitForIdleSync();
                screenshot(test,"language-selector-"+code+".png");
                var monitor=test.addMonitor(MainActivity.class.getName(),null,false);
                test.runOnMainSync(()->dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick());
                Activity next=monitor.waitForActivityWithTimeout(10000);test.removeMonitor(monitor);
                if(next==null)throw new AssertionError("Activity was not recreated");activity=next;
                test.waitForIdleSync();
                if(!code.equals(context.getSharedPreferences("display-language",0).getString("code","ja")))throw new AssertionError("Saved selection");
                final Activity updated=activity;
                test.runOnMainSync(()->{
                    if(!containsDescription(updated.getWindow().getDecorView(),code.equals("en")?"Settings":"設定"))throw new AssertionError("Header translation");
                });
                screenshot(test,"language-main-"+code+".png");
            }
        }finally{
            test.runOnMainSync(()->PlayerApplication.saveLanguage(context,original));
            final Activity last=activity;test.runOnMainSync(last::finish);
        }
    }
    private static boolean containsDescription(View view,String expected){
        if(expected.contentEquals(view.getContentDescription()==null?"":view.getContentDescription()))return true;
        if(view instanceof ViewGroup){var group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++)if(containsDescription(group.getChildAt(i),expected))return true;}
        return false;
    }
    private static void screenshot(Instrumentation test,String name)throws IOException{
        Bitmap bitmap=test.getUiAutomation().takeScreenshot();if(bitmap==null)throw new AssertionError("Screenshot unavailable");
        try(var out=new FileOutputStream(new File(test.getTargetContext().getCacheDir(),name))){bitmap.compress(Bitmap.CompressFormat.PNG,100,out);}finally{bitmap.recycle();}
    }
}
