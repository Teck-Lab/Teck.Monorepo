import '../../global.css';

import { StatusBar } from 'expo-status-bar';
import { Text, View } from 'react-native';

export const App = () => {
  return (
    <View className="flex-1 items-center justify-center gap-2 bg-background">
      <Text className="text-2xl font-medium text-foreground">@teck/mobile</Text>
      <Text className="text-foreground">NativeWind v5 · shared tokens</Text>
      <StatusBar style="auto" />
    </View>
  );
};

export default App;
