package jp.virtualcd.player;

public final class PlayerApplication extends android.app.Application {
    @Override public void onCreate(){super.onCreate();LanguageStrings.setCode(getSharedPreferences("display-language",0).getString("code","ja"));}
    public static void saveLanguage(android.content.Context context,String code){
        String language="en".equals(code)?"en":"ja";
        context.getSharedPreferences("display-language",0).edit().putString("code",language).apply();
        LanguageStrings.setCode(language);
    }
}
